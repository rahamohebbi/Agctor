using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Processing;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Parses extractor JSON, routes and projects intents, surfaces out-of-schema proposals to the generic inbox,
/// and handles user confirm/reject for pending inbox rows. Shared by <see cref="ProjectMemoryPipelineRunner"/> and tests.
/// </summary>
public sealed class ProjectMemoryIngestService
{
    private readonly IEntityRegistry _entities;
    private readonly IMemoryIntentProcessor _processor;
    private readonly IDocumentProjectionService _projection;
    private readonly IGenericInboxStore _genericInbox;
    private readonly IGenericInboxReplayService? _replay;
    private readonly MentionObservationPublisher? _mentions;

    /// <summary>Same dependencies as the pipeline runner’s ingest path (optional mentions for resolution bridge).</summary>
    public ProjectMemoryIngestService(
        IEntityRegistry entities,
        IMemoryIntentProcessor processor,
        IDocumentProjectionService projection,
        IGenericInboxStore genericInbox,
        IGenericInboxReplayService? replay = null,
        MentionObservationPublisher? mentions = null)
    {
        _entities = entities;
        _processor = processor;
        _projection = projection;
        _genericInbox = genericInbox;
        _replay = replay;
        _mentions = mentions;
    }

    /// <summary>End-to-end ingest from raw LLM JSON: parse → normalize → route → project; may append generic-inbox pending rows.</summary>
    public async Task<RawIngestWork> IngestFromRawExtractAsync(
        LoadedProjectContext ctx,
        string rawExtract,
        string entityWorkspaceRoot,
        string? sessionId,
        string? turnId,
        CancellationToken cancellationToken)
    {
        if (!MemoryIntentJson.TryParseBatch(rawExtract, out var batch, out var parseErr, out var parseSource))
            return new RawIngestWork(false, parseErr, null, false, false, "", false, new List<string>(), null, Array.Empty<OutOfSchemaFactProposal>());

        if (batch!.MemoryIntents.Count == 0)
        {
            return new RawIngestWork(
                true,
                null,
                parseSource,
                true,
                false,
                "No memory intents; skipped write.",
                false,
                new List<string>(),
                null,
                Array.Empty<OutOfSchemaFactProposal>());
        }

        var discovered = await _entities.DiscoverAsync(ctx, entityWorkspaceRoot, cancellationToken).ConfigureAwait(false);
        var familyReferencedRaw = CollectFamilyReferencedEntityKeys(batch.MemoryIntents);
        var familyNotes = new List<string>();
        FamilyRoleIntentNormalizer.Apply(batch.MemoryIntents, discovered, rawExtract, familyNotes);

        if (batch.MemoryIntents.Count == 0)
        {
            var dropped = familyNotes.Count > 0
                ? "All intents dropped after family_role normalization: " + string.Join("; ", familyNotes)
                : "All intents dropped after family_role normalization.";
            return new RawIngestWork(true, null, parseSource, true, false, dropped, false, new List<string>(), null, Array.Empty<OutOfSchemaFactProposal>());
        }

        if (_mentions != null)
        {
            try
            {
                var scenarioId = TryExtractScenarioId(ctx, entityWorkspaceRoot);
                var mentions = MentionObservationPublisher.FromMemoryIntents(batch.MemoryIntents, scenarioId, sessionId, turnId);
                if (mentions.Count > 0)
                    await _mentions.PublishAsync(ProjectIdFor(ctx), mentions, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        var routed = _processor.Route(ctx, batch.MemoryIntents, out var routeIssues);
        var oosProposals = OutOfSchemaProposalFactory.FromRouteIssues(routeIssues, ctx.Runtime.OutOfSchema).ToList();
        if (oosProposals.Count > 0)
        {
            var scen = TryExtractScenarioId(ctx, entityWorkspaceRoot);
            await _genericInbox.AppendPendingAsync(ctx.ProjectRoot, scen, oosProposals, cancellationToken).ConfigureAwait(false);
        }

        var routeErrors = routeIssues.Where(i => i.IsError).ToList();
        if (routeErrors.Count > 0 && routed.Count == 0)
        {
            if (oosProposals.Count > 0)
            {
                return new RawIngestWork(
                    true, null, parseSource, false, false,
                    "No routable intents; " + oosProposals.Count + " out-of-schema fact(s) await user confirmation.",
                    false, new List<string>(), null, oosProposals);
            }

            return new RawIngestWork(
                true, null, parseSource, false, true,
                string.Join("; ", routeErrors.Select(i => i.Message)),
                false, new List<string>(), null, oosProposals);
        }

        var routeDetail = $"Routed {routed.Count} intent(s).";
        if (familyNotes.Count > 0) routeDetail = string.Join("; ", familyNotes) + " | " + routeDetail;
        if (routeErrors.Count > 0)
            routeDetail += " Skipped " + routeErrors.Count + " unroutable intent(s): " + string.Join("; ", routeErrors.Select(i => i.Message));
        if (oosProposals.Count > 0)
            routeDetail += " | Out-of-schema: " + oosProposals.Count + " fact(s) surfaced for confirmation (see ingest metadata).";

        var groups = routed.GroupBy(r => r.Original.EntityKey, StringComparer.OrdinalIgnoreCase).ToList();
        var lookup = BuildEntityLookup(discovered);
        var updated = new List<string>();

        var bootstrapped = false;
        foreach (var g in groups)
        {
            if (ResolveEntityRecord(g.Key, discovered, lookup) != null) continue;
            var created = await EntityFolderBootstrapper.TryCreateIfMissingAsync(ctx, entityWorkspaceRoot, g.Key, g.ToList(), cancellationToken).ConfigureAwait(false);
            if (created.Count > 0)
            {
                updated.AddRange(created);
                bootstrapped = true;
            }
        }

        foreach (var (entityKey, displayNameHint) in familyReferencedRaw)
        {
            if (ResolveEntityRecord(entityKey, discovered, lookup) != null) continue;
            if (groups.Any(g => string.Equals(g.Key, entityKey, StringComparison.OrdinalIgnoreCase))) continue;
            var synthetic = SyntheticNameIntents(entityKey, displayNameHint);
            var created = await EntityFolderBootstrapper.TryCreateIfMissingAsync(ctx, entityWorkspaceRoot, entityKey, synthetic, cancellationToken).ConfigureAwait(false);
            if (created.Count > 0)
            {
                updated.AddRange(created);
                bootstrapped = true;
            }
        }

        if (bootstrapped)
        {
            discovered = await _entities.DiscoverAsync(ctx, entityWorkspaceRoot, cancellationToken).ConfigureAwait(false);
            lookup = BuildEntityLookup(discovered);
        }

        var unresolved = new List<string>();
        foreach (var g in groups)
        {
            var rec = ResolveEntityRecord(g.Key, discovered, lookup);
            if (rec == null)
            {
                unresolved.Add(g.Key);
                continue;
            }

            var res = await _projection.ApplyAsync(rec, g.ToList(), cancellationToken).ConfigureAwait(false);
            updated.AddRange(res.UpdatedFiles);
        }

        var writeOk = updated.Count > 0;
        var writeDetail = unresolved.Count > 0
            ? "Updated " + updated.Count + " file(s); unresolved entity keys: " + string.Join(", ", unresolved.Distinct(StringComparer.OrdinalIgnoreCase))
            : (updated.Count == 0 ? "No files updated (entities not found?)." : null);
        return new RawIngestWork(true, null, parseSource, false, false, routeDetail, writeOk, updated, writeDetail, oosProposals);
    }

    /// <summary>If recent pending generic-inbox rows match the request scenario, apply reject or persist-approved and append pipeline steps.</summary>
    public async Task<ConfirmationHandling> TryHandleConfirmationAsync(
        LoadedProjectContext ctx,
        ProjectMemoryPipelineRequest request,
        ConfirmationInputDetector.ConfirmationSignal signal,
        List<ProjectMemoryPipelineStep> steps,
        CancellationToken cancellationToken)
    {
        var pending = await _genericInbox.LoadPendingAsync(ctx.ProjectRoot, cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0) return new ConfirmationHandling(false, false, "");

        var scenSeg = string.IsNullOrWhiteSpace(request.ScenarioId) ? "" : PersonaScenarioScope.SanitizeFolderSegment(request.ScenarioId);
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);

        var candidates = new List<GenericInboxPendingRow>();
        var scenarioRows = new List<(GenericInboxPendingRow Row, DateTimeOffset QueuedAt)>();
        foreach (var row in pending)
        {
            if (!string.Equals(row.ScenarioSegment ?? "", scenSeg, StringComparison.OrdinalIgnoreCase)) continue;
            if (!DateTimeOffset.TryParse(row.QueuedAtUtc, out var queuedAt)) continue;
            scenarioRows.Add((row, queuedAt));
            if (queuedAt < cutoff) continue;
            candidates.Add(row);
        }

        if (candidates.Count == 0)
        {
            if (scenarioRows.Count == 0) return new ConfirmationHandling(false, false, "");

            // If a visible prompt is backed by an older duplicate row (for example after a Host restart
            // or before duplicate pending rows were refreshed), confirm only the latest pending batch for
            // this scenario instead of falling through to extractor/curator and reporting "no files".
            var latest = scenarioRows.Max(x => x.QueuedAt);
            candidates = scenarioRows
                .Where(x => x.QueuedAt == latest)
                .Select(x => x.Row)
                .ToList();
            steps.Add(new ProjectMemoryPipelineStep
            {
                Name = "confirm-window",
                Ok = true,
                Detail = "No recent pending rows matched; using latest pending generic-inbox row(s) for this scenario."
            });
        }

        if (signal == ConfirmationInputDetector.ConfirmationSignal.Negative)
        {
            var dropped = await _genericInbox.DropPendingAsync(ctx.ProjectRoot, candidates.Select(c => c.ProposalId).ToList(), cancellationToken).ConfigureAwait(false);
            steps.Add(new ProjectMemoryPipelineStep { Name = "confirm", Ok = true, Detail = "rejected " + dropped + " pending generic-inbox row(s)." });
            return new ConfirmationHandling(true, true, "Got it — I will not store " + dropped + " out-of-schema fact(s). You can always re-enter them later.");
        }

        var approvals = candidates.Select(r => new ApprovedGenericFact
        {
            ProposalId = r.ProposalId,
            EntityKey = r.EntityKey,
            KnowledgeType = r.KnowledgeType,
            Attribute = r.Attribute,
            Value = r.Value,
            Confidence = r.Confidence
        }).ToList();

        var result = await _genericInbox.PersistApprovedAsync(ctx.ProjectRoot, string.IsNullOrEmpty(scenSeg) ? null : scenSeg, approvals, cancellationToken).ConfigureAwait(false);

        var learnLines = ApprovedFactRoutingLearner.TryLearnAfterPersist(ctx, result, approvals);

        // After we may have just learned new routing rules, back-fill the entity files from confirmed.yaml.
        // This lets the user immediately see the approved fact land on profile.md (or wherever the new rule routes).
        GenericInboxReplayReport? replayReport = null;
        if (_replay != null && result.Appended > 0)
        {
            try
            {
                replayReport = await _replay
                    .ReplayAsync(ctx.ProjectRoot, scenarioId: string.IsNullOrEmpty(scenSeg) ? null : scenSeg, options: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                steps.Add(new ProjectMemoryPipelineStep { Name = "confirm-replay", Ok = false, Detail = "replay failed: " + ex.Message });
            }
        }

        var msg = new StringBuilder();
        msg.Append("Stored ").Append(result.Appended).Append(" fact(s) in the generic inbox (.agctor/runtime/generic-inbox/confirmed.yaml). ");
        if (result.Appended > 0)
        {
            msg.AppendLine();
            foreach (var a in approvals.Take(10))
            {
                var attr = string.IsNullOrWhiteSpace(a.Attribute) ? "" : "/" + a.Attribute;
                msg.Append("- ").Append(a.EntityKey).Append(" · ").Append(a.KnowledgeType).Append(attr).Append(" = ").AppendLine(a.Value);
            }
            if (approvals.Count > 10) msg.Append("(+").Append(approvals.Count - 10).AppendLine(" more)");
        }

        if (result.RejectedMismatch > 0) msg.Append(" (").Append(result.RejectedMismatch).Append(" hash mismatches skipped)");

        if (learnLines.Count > 0)
        {
            msg.AppendLine();
            foreach (var line in learnLines)
                msg.AppendLine(line);
        }

        if (replayReport is { Routed: > 0 } && replayReport.UpdatedFiles.Count > 0)
        {
            msg.AppendLine();
            msg.Append("Back-filled ").Append(replayReport.Routed).Append(" approved fact(s) into entity files:");
            msg.AppendLine();
            foreach (var f in replayReport.UpdatedFiles.Take(10))
                msg.Append("- ").AppendLine(ToProjectRelative(ctx.ProjectRoot, f));
            if (replayReport.UpdatedFiles.Count > 10)
                msg.Append("(+").Append(replayReport.UpdatedFiles.Count - 10).AppendLine(" more)");
        }

        var confirmDetail = "persisted " + result.Appended + " generic-inbox row(s); rejected " + result.RejectedMismatch + ".";
        if (learnLines.Count > 0)
            confirmDetail += " Auto-appended " + learnLines.Count + " routing-rule line(s) to schemas.";
        if (replayReport != null)
            confirmDetail += " Replay routed " + replayReport.Routed + " row(s); skipped " + replayReport.SkippedRouteMiss + " unrouted; updated " + replayReport.UpdatedFiles.Count + " file(s).";

        steps.Add(new ProjectMemoryPipelineStep
        {
            Name = "confirm",
            Ok = result.Appended > 0,
            Detail = confirmDetail
        });

        if (replayReport != null)
        {
            steps.Add(new ProjectMemoryPipelineStep
            {
                Name = "confirm-replay",
                Ok = true,
                UpdatedFiles = replayReport.UpdatedFiles,
                Detail = $"considered={replayReport.Considered}; routed={replayReport.Routed}; routeMiss={replayReport.SkippedRouteMiss}; alreadyReplayed={replayReport.SkippedAlreadyReplayed}; unresolved={replayReport.SkippedUnresolvedEntity}"
            });
        }

        return new ConfirmationHandling(true, result.Appended > 0 || approvals.Count == 0, msg.ToString().TrimEnd());
    }

    /// <summary>Entity keys referenced by <c>family_role</c> intents (for bootstrapping folders when only relatives are named).</summary>
    private static IReadOnlyList<(string EntityKey, string DisplayNameHint)> CollectFamilyReferencedEntityKeys(IReadOnlyList<MemoryIntent> intents)
    {
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (intents == null) return Array.Empty<(string, string)>();
        foreach (var intent in intents)
        {
            if (intent == null) continue;
            if (!string.Equals(intent.KnowledgeType, "family_role", StringComparison.OrdinalIgnoreCase)) continue;
            AddFamilyToken(byKey, intent.EntityKey);
            AddFamilyToken(byKey, intent.Value);
        }
        return byKey.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Adds a folder slug → display token pair when absent (dedupes by slug).</summary>
    private static void AddFamilyToken(Dictionary<string, string> byKey, string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var slug = EntityFolderBootstrapper.SlugFolderSegment(rawToken);
        if (string.IsNullOrEmpty(slug)) return;
        if (byKey.ContainsKey(slug)) return;
        byKey[slug] = rawToken.Trim();
    }

    /// <summary>Minimal routed intents so <see cref="EntityFolderBootstrapper"/> can create a person folder with a display name.</summary>
    private static IReadOnlyList<RoutedMemoryIntent> SyntheticNameIntents(string entityKey, string displayNameHint)
    {
        var display = string.IsNullOrWhiteSpace(displayNameHint)
            ? (entityKey.Length == 0 ? entityKey : char.ToUpperInvariant(entityKey[0]) + entityKey[1..])
            : displayNameHint.Trim();
        return new List<RoutedMemoryIntent>
        {
            new()
            {
                Original = new MemoryIntent
                {
                    EntityKey = entityKey,
                    KnowledgeType = "profile_fact",
                    Attribute = "name",
                    Value = display,
                    Confidence = 1.0
                },
                DocumentTypeId = "profile",
                SectionTitle = "Basic Info",
                UpdateMode = "replace_section",
                FileName = "profile.md"
            }
        };
    }

    /// <summary>First segment under <c>scenarios/</c> when workspace lives under project scenarios (persona scope).</summary>
    private static string? TryExtractScenarioId(LoadedProjectContext ctx, string entityWorkspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(entityWorkspaceRoot)) return null;
        var scenariosDir = Path.Combine(ctx.ProjectRoot, "scenarios");
        var full = Path.GetFullPath(entityWorkspaceRoot);
        if (!full.StartsWith(scenariosDir, StringComparison.OrdinalIgnoreCase)) return null;
        var rel = Path.GetRelativePath(scenariosDir, full);
        var first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    /// <summary>Stable id for mention publisher (folder name of project root).</summary>
    private static string ProjectIdFor(LoadedProjectContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.ProjectRoot)
            ? "default"
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(ctx.ProjectRoot));

    /// <summary>Resolves an entity from discovery + normalized keys (handles path-like keys and folder names).</summary>
    private static EntityRecord? ResolveEntityRecord(string rawEntityKey, IReadOnlyList<EntityRecord> discovered, Dictionary<string, EntityRecord> lookup)
    {
        if (string.IsNullOrWhiteSpace(rawEntityKey)) return null;
        if (lookup.TryGetValue(NormalizeEntityToken(rawEntityKey), out var byRaw)) return byRaw;

        var parts = rawEntityKey.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var p = NormalizeEntityToken(parts[i]);
            if (lookup.TryGetValue(p, out var byPart)) return byPart;
        }

        var fallback = NormalizeEntityToken(Path.GetFileName(rawEntityKey));
        return lookup.TryGetValue(fallback, out var byFileName) ? byFileName : null;
    }

    /// <summary>Maps normalized tokens (key, display name, aliases, folder name) to <see cref="EntityRecord"/> for ingest matching.</summary>
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

    /// <summary>Inserts first-wins normalized token → record (ignores empty tokens).</summary>
    private static void AddLookup(Dictionary<string, EntityRecord> map, string? raw, EntityRecord rec)
    {
        var k = NormalizeEntityToken(raw);
        if (string.IsNullOrEmpty(k)) return;
        if (!map.ContainsKey(k)) map[k] = rec;
    }

    /// <summary>Lowercase letters+digits only — aligns ingest keys with bootstrapper slugs.</summary>
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

    /// <summary>Make user-facing file paths project-relative so logs and assertions stay machine-stable.</summary>
    private static string ToProjectRelative(string projectRoot, string absolutePath)
    {
        try
        {
            var pr = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var p = Path.GetFullPath(absolutePath);
            if (p.StartsWith(pr + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, pr, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(pr, p).Replace('\\', '/');
        }
        catch
        {
        }

        return absolutePath;
    }
}

/// <summary>Outcome of one ingest pass (parse, route, write, and any out-of-schema proposals queued).</summary>
public sealed record RawIngestWork(
    bool ParseOk,
    string? ParseError,
    string? ParseSource,
    bool NoIntents,
    bool RouteFatal,
    string RouteDetail,
    bool WriteOk,
    List<string> UpdatedFiles,
    string? WriteDetail,
    IReadOnlyList<OutOfSchemaFactProposal> OutOfSchemaProposals);

/// <summary>Whether confirmation flow ran, whether it should count as pipeline success, and user-facing reply text.</summary>
public sealed record ConfirmationHandling(bool Handled, bool Success, string FinalText);
