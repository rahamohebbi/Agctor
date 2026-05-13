using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Processing;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>
/// Default replay implementation: loads project context, rereads <c>routing-rules.yaml</c> via
/// <see cref="IProjectLoader"/>, runs each confirmed row through <see cref="IMemoryIntentProcessor"/>, then
/// projects matched intents through <see cref="IDocumentProjectionService"/> against entities discovered
/// under the appropriate workspace (project root or <c>scenarios/&lt;seg&gt;/</c>).
/// </summary>
public sealed class GenericInboxReplayService : IGenericInboxReplayService
{
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;
    private readonly IMemoryIntentProcessor _processor;
    private readonly IDocumentProjectionService _projection;
    private readonly IGenericInboxStore _store;

    public GenericInboxReplayService(
        IProjectLoader loader,
        IEntityRegistry entities,
        IMemoryIntentProcessor processor,
        IDocumentProjectionService projection,
        IGenericInboxStore store)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<GenericInboxReplayReport> ReplayAsync(
        string projectRoot,
        string? scenarioId = null,
        GenericInboxReplayOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GenericInboxReplayOptions();
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(Path.Combine(projectRoot, ".agctor")))
        {
            issues.Add(new ValidationIssue { Code = "no_project_root", Message = "Project root must contain a .agctor directory.", IsError = true });
            return new GenericInboxReplayReport { Issues = issues };
        }

        // Reload context so newly-learned routing rules are visible during replay.
        var ctx = await _loader.LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);

        var rows = await _store.LoadConfirmedAsync(ctx.ProjectRoot, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
            return new GenericInboxReplayReport();

        var scenarioFilter = string.IsNullOrWhiteSpace(scenarioId)
            ? null
            : PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
        var entityFilter = options.OnlyEntityKeys?.Select(k => k.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ktFilter = options.OnlyKnowledgeTypes?.Select(k => k.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var considered = new List<GenericInboxConfirmedRow>();
        var skippedAlreadyReplayed = 0;
        foreach (var row in rows)
        {
            if (scenarioFilter != null && !string.Equals(row.ScenarioSegment ?? "", scenarioFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (entityFilter != null && !entityFilter.Contains(row.EntityKey))
                continue;
            if (ktFilter != null && !ktFilter.Contains(row.KnowledgeType))
                continue;
            if (!options.IncludeAlreadyReplayed && !string.IsNullOrWhiteSpace(row.ReplayedAtUtc))
            {
                skippedAlreadyReplayed++;
                continue;
            }

            considered.Add(row);
        }

        if (considered.Count == 0)
        {
            return new GenericInboxReplayReport
            {
                Considered = 0,
                SkippedAlreadyReplayed = skippedAlreadyReplayed
            };
        }

        // Group rows by scenario segment so each batch is projected against its own workspace.
        var bySegment = considered.GroupBy(r => r.ScenarioSegment ?? "", StringComparer.OrdinalIgnoreCase);

        var routedTotal = 0;
        var routeMissTotal = 0;
        var unresolvedTotal = 0;
        var stampedIds = new List<string>();
        var updatedFiles = new List<string>();

        foreach (var group in bySegment)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seg = string.IsNullOrWhiteSpace(group.Key) ? null : group.Key;
            var workspaceRoot = PersonaScenarioScope.GetEntityWorkspaceRoot(ctx.ProjectRoot, seg);
            if (!PersonaScenarioScope.IsUnderProjectRoot(ctx.ProjectRoot, workspaceRoot))
            {
                issues.Add(new ValidationIssue { Code = "scope", Message = $"Replay skipped for invalid scenario scope '{seg}'.", IsError = true });
                continue;
            }

            // Convert to MemoryIntents and route.
            var intentRows = group.ToList();
            var intents = intentRows.Select(r => new MemoryIntent
            {
                EntityKey = r.EntityKey,
                KnowledgeType = r.KnowledgeType,
                Attribute = r.Attribute,
                Value = r.Value,
                Confidence = r.Confidence
            }).ToList();

            var routed = _processor.Route(ctx, intents, out var routeIssues);
            foreach (var ri in routeIssues.Where(i => string.Equals(i.Code, "route_miss", StringComparison.OrdinalIgnoreCase)))
            {
                routeMissTotal++;
                issues.Add(ri);
            }

            if (routed.Count == 0)
                continue;

            // Map routed → original confirmed row to preserve proposalId for the replay stamp.
            // Routed.Original is the same MemoryIntent reference we built above; we line up by index in `intents`.
            var routedToProposalId = new List<(RoutedMemoryIntent Routed, string ProposalId)>(routed.Count);
            foreach (var r in routed)
            {
                var idx = intents.IndexOf(r.Original);
                if (idx >= 0 && idx < intentRows.Count)
                    routedToProposalId.Add((r, intentRows[idx].ProposalId));
            }

            var discovered = await _entities.DiscoverAsync(ctx, workspaceRoot, cancellationToken).ConfigureAwait(false);
            var lookup = BuildEntityLookup(discovered);

            // Bootstrap missing folders so projection has a target (mirrors ingest behavior).
            var byEntity = routedToProposalId.GroupBy(r => r.Routed.Original.EntityKey, StringComparer.OrdinalIgnoreCase).ToList();
            var bootstrapped = false;
            foreach (var g in byEntity)
            {
                if (ResolveEntityRecord(g.Key, lookup) != null) continue;
                var created = await EntityFolderBootstrapper
                    .TryCreateIfMissingAsync(ctx, workspaceRoot, g.Key, g.Select(x => x.Routed).ToList(), cancellationToken)
                    .ConfigureAwait(false);
                if (created.Count > 0)
                {
                    updatedFiles.AddRange(created);
                    bootstrapped = true;
                }
            }

            if (bootstrapped)
            {
                discovered = await _entities.DiscoverAsync(ctx, workspaceRoot, cancellationToken).ConfigureAwait(false);
                lookup = BuildEntityLookup(discovered);
            }

            foreach (var g in byEntity)
            {
                var rec = ResolveEntityRecord(g.Key, lookup);
                if (rec == null)
                {
                    unresolvedTotal++;
                    issues.Add(new ValidationIssue { Code = "unresolved-entity", Message = $"Replay could not resolve entity '{g.Key}' under '{workspaceRoot}'.", IsError = true });
                    continue;
                }

                try
                {
                    var groupRouted = g.Select(x => x.Routed).ToList();
                    var res = await _projection.ApplyAsync(rec, groupRouted, cancellationToken).ConfigureAwait(false);
                    updatedFiles.AddRange(res.UpdatedFiles);
                    routedTotal += groupRouted.Count;
                    stampedIds.AddRange(g.Select(x => x.ProposalId));
                }
                catch (Exception ex)
                {
                    issues.Add(new ValidationIssue { Code = "projection-error", Message = ex.Message, IsError = true });
                }
            }
        }

        if (stampedIds.Count > 0)
        {
            var nowIso = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            await _store.MarkReplayedAsync(ctx.ProjectRoot, stampedIds, nowIso, cancellationToken).ConfigureAwait(false);
        }

        return new GenericInboxReplayReport
        {
            Considered = considered.Count,
            Routed = routedTotal,
            SkippedAlreadyReplayed = skippedAlreadyReplayed,
            SkippedRouteMiss = routeMissTotal,
            SkippedUnresolvedEntity = unresolvedTotal,
            UpdatedFiles = updatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Issues = issues
        };
    }

    /// <summary>Maps normalized tokens (key, display name, aliases, folder name) → entity for resolution.</summary>
    private static Dictionary<string, EntityRecord> BuildEntityLookup(IReadOnlyList<EntityRecord> discovered)
    {
        var map = new Dictionary<string, EntityRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in discovered)
        {
            AddLookup(map, e.EntityKey, e);
            AddLookup(map, e.Metadata.DisplayName, e);
            if (e.Metadata.Aliases != null)
            {
                foreach (var a in e.Metadata.Aliases) AddLookup(map, a, e);
            }
            AddLookup(map, Path.GetFileName(e.RootPath), e);
        }
        return map;
    }

    private static void AddLookup(Dictionary<string, EntityRecord> map, string? raw, EntityRecord rec)
    {
        var k = NormalizeEntityToken(raw);
        if (string.IsNullOrEmpty(k)) return;
        if (!map.ContainsKey(k)) map[k] = rec;
    }

    private static EntityRecord? ResolveEntityRecord(string rawEntityKey, Dictionary<string, EntityRecord> lookup)
    {
        if (string.IsNullOrWhiteSpace(rawEntityKey)) return null;
        if (lookup.TryGetValue(NormalizeEntityToken(rawEntityKey), out var byRaw)) return byRaw;

        var parts = rawEntityKey.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var p = NormalizeEntityToken(parts[i]);
            if (lookup.TryGetValue(p, out var byPart)) return byPart;
        }

        return null;
    }

    private static string NormalizeEntityToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim().ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }
}
