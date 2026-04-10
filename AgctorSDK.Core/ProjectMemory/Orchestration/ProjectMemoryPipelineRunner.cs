using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Processing;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Code-first orchestrator: chains person-extractor → routing/projection → person-query without actor envelopes (same file effects as the dedicated agents).
/// </summary>
public sealed class ProjectMemoryPipelineRunner : IProjectMemoryPipelineRunner
{
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;
    private readonly IMemoryIntentProcessor _processor;
    private readonly IDocumentProjectionService _projection;
    private readonly ProjectMemoryOperations _ops;
    private readonly IProjectMemoryLlmClient _llm;

    public ProjectMemoryPipelineRunner(
        IProjectLoader loader,
        IEntityRegistry entities,
        IMemoryIntentProcessor processor,
        IDocumentProjectionService projection,
        ProjectMemoryOperations ops,
        IProjectMemoryLlmClient llm)
    {
        _loader = loader;
        _entities = entities;
        _processor = processor;
        _projection = projection;
        _ops = ops;
        _llm = llm;
    }

    /// <inheritdoc />
    public async Task<ProjectMemoryPipelineResult> RunAsync(ProjectMemoryPipelineRequest request, CancellationToken cancellationToken = default)
    {
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();

        var steps = new List<ProjectMemoryPipelineStep>();
        var root = Path.GetFullPath(request.ProjectRoot.Trim());
        if (!Directory.Exists(Path.Combine(root, ".agctor")))
        {
            return Fail(correlationId, "Project root must contain a .agctor directory.", steps);
        }

        var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
        var extractorSpec = ctx.AgentSpecs.FirstOrDefault(a =>
            string.Equals(a.Id, "person-extractor", StringComparison.OrdinalIgnoreCase));
        var querySpec = ctx.AgentSpecs.FirstOrDefault(a =>
            string.Equals(a.Id, "person-query", StringComparison.OrdinalIgnoreCase));

        var mode = request.Mode;
        if (mode != ProjectMemoryPipelineMode.QueryOnly && extractorSpec == null)
            return Fail(correlationId, "person-extractor agent spec missing.", steps);
        if (mode != ProjectMemoryPipelineMode.IngestOnly && querySpec == null)
            return Fail(correlationId, "person-query agent spec missing.", steps);

        var success = true;
        string? rawExtract = null;

        if (mode != ProjectMemoryPipelineMode.QueryOnly)
        {
            try
            {
                var extractPrompt = BuildExtractPrompt(extractorSpec!, request.UserMessage, request.ConversationPrefix);
                rawExtract = await _llm.GenerateAsync(extractPrompt, cancellationToken).ConfigureAwait(false);
                steps.Add(new ProjectMemoryPipelineStep
                {
                    Name = "extract",
                    Ok = true,
                    // Keep full extractor JSON so operators can inspect all intents in the timeline UI.
                    Detail = rawExtract
                });
            }
            catch (Exception ex)
            {
                success = false;
                steps.Add(new ProjectMemoryPipelineStep { Name = "extract", Ok = false, Detail = ex.Message });
                if (mode == ProjectMemoryPipelineMode.IngestOnly)
                {
                    return Finish(correlationId, false, "Extract failed: " + ex.Message, steps);
                }
                // Auto: continue to query without ingest
            }

            if (rawExtract != null &&
                !await TryIngestFromExtractAsync(ctx, rawExtract, steps, cancellationToken).ConfigureAwait(false))
            {
                success = false;
                if (mode == ProjectMemoryPipelineMode.IngestOnly)
                {
                    var msg = steps.LastOrDefault(s => !s.Ok)?.Detail ?? "Ingest failed.";
                    return Finish(correlationId, false, msg, steps);
                }
            }
        }

        if (mode == ProjectMemoryPipelineMode.IngestOnly)
        {
            return Finish(correlationId, success, success ? "Ingest completed." : "Ingest failed; see steps.", steps);
        }

        try
        {
            var answer = await RunQueryAsync(querySpec!, root, request.UserMessage, request.ConversationPrefix, cancellationToken)
                .ConfigureAwait(false);
            steps.Add(new ProjectMemoryPipelineStep { Name = "query", Ok = true, Detail = Truncate(answer, 600) });
            return Finish(correlationId, success, answer, steps);
        }
        catch (Exception ex)
        {
            success = false;
            steps.Add(new ProjectMemoryPipelineStep { Name = "query", Ok = false, Detail = ex.Message });
            return Finish(correlationId, false, "Query failed: " + ex.Message, steps);
        }
    }

    /// <summary>Returns false if ingest path failed (parse, route, or write).</summary>
    private async Task<bool> TryIngestFromExtractAsync(
        LoadedProjectContext ctx,
        string rawExtract,
        List<ProjectMemoryPipelineStep> steps,
        CancellationToken cancellationToken)
    {
        if (!MemoryIntentJson.TryParseBatch(rawExtract, out var batch, out var parseErr))
        {
            steps.Add(new ProjectMemoryPipelineStep { Name = "parse", Ok = false, Detail = parseErr });
            return false;
        }

        if (batch!.MemoryIntents.Count == 0)
        {
            steps.Add(new ProjectMemoryPipelineStep { Name = "route", Ok = true, Detail = "No memory intents; skipped write." });
            return true;
        }

        var routed = _processor.Route(ctx, batch.MemoryIntents, out var routeIssues);
        var routeErrors = routeIssues.Where(i => i.IsError).ToList();
        if (routeErrors.Count > 0 && routed.Count == 0)
        {
            steps.Add(new ProjectMemoryPipelineStep
            {
                Name = "route",
                Ok = false,
                Detail = string.Join("; ", routeErrors.Select(i => i.Message))
            });
            return false;
        }

        var routeDetail = $"Routed {routed.Count} intent(s).";
        if (routeErrors.Count > 0)
            routeDetail += " Skipped " + routeErrors.Count + " unroutable intent(s): " +
                           string.Join("; ", routeErrors.Select(i => i.Message));
        steps.Add(new ProjectMemoryPipelineStep
        {
            Name = "route",
            Ok = true,
            Detail = routeDetail
        });

        var discovered = await _entities.DiscoverAsync(ctx, cancellationToken).ConfigureAwait(false);
        var lookup = BuildEntityLookup(discovered);
        var byEntity = routed.GroupBy(r => r.Original.EntityKey, StringComparer.OrdinalIgnoreCase);
        var updated = new List<string>();
        var unresolved = new List<string>();
        foreach (var g in byEntity)
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
        steps.Add(new ProjectMemoryPipelineStep
        {
            Name = "write",
            Ok = writeOk,
            UpdatedFiles = updated,
            Detail = writeDetail
        });
        return writeOk;
    }

    private async Task<string> RunQueryAsync(
        AgentDefinitionSpec querySpec,
        string projectRoot,
        string userMessage,
        string? conversationPrefix,
        CancellationToken cancellationToken)
    {
        var hits = await _ops.SearchEntitiesAsync(projectRoot, null, cancellationToken).ConfigureAwait(false);
        var sb = new StringBuilder();
        foreach (var h in hits.Take(20))
        {
            var profile = await _ops.ReadDocumentAsync(querySpec, projectRoot, $"people/{h.EntityKey}/profile.md", cancellationToken)
                .ConfigureAwait(false);
            sb.AppendLine($"### {h.EntityKey}");
            sb.AppendLine(profile);
            sb.AppendLine();
        }

        var instructions = string.Join('\n', querySpec.Instructions ?? new List<string>());
        var prefix = string.IsNullOrWhiteSpace(conversationPrefix)
            ? ""
            : "Prior conversation:\n" + conversationPrefix.Trim() + "\n---\n";
        var prompt = instructions + "\nContext:\n" + sb + "\n" + prefix + "Question:\n" + userMessage;
        return await _llm.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildExtractPrompt(AgentDefinitionSpec spec, string userMessage, string? conversationPrefix)
    {
        var lines = (spec.Instructions ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i));
        var sys = string.Join('\n', lines)
                    + "\n\nRespond with ONLY valid JSON: {\"memoryIntents\":[{\"entityKey\":\"\",\"knowledgeType\":\"\",\"attribute\":\"\",\"value\":\"\",\"confidence\":0.9}]}\n";
        if (!string.IsNullOrWhiteSpace(conversationPrefix))
            sys += "\nPrior conversation:\n" + conversationPrefix.Trim() + "\n---\n";
        return sys + "\nInput:\n" + userMessage;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    private static EntityRecord? ResolveEntityRecord(
        string rawEntityKey,
        IReadOnlyList<EntityRecord> discovered,
        Dictionary<string, EntityRecord> lookup)
    {
        if (string.IsNullOrWhiteSpace(rawEntityKey))
            return null;

        if (lookup.TryGetValue(NormalizeEntityToken(rawEntityKey), out var byRaw))
            return byRaw;

        var parts = rawEntityKey.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var p = NormalizeEntityToken(parts[i]);
            if (lookup.TryGetValue(p, out var byPart))
                return byPart;
        }

        var fallback = NormalizeEntityToken(Path.GetFileName(rawEntityKey));
        return lookup.TryGetValue(fallback, out var byFileName) ? byFileName : null;
    }

    private static Dictionary<string, EntityRecord> BuildEntityLookup(IReadOnlyList<EntityRecord> discovered)
    {
        var map = new Dictionary<string, EntityRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in discovered)
        {
            AddLookup(map, e.EntityKey, e);
            AddLookup(map, e.Metadata.DisplayName, e);
            if (e.Metadata.Aliases != null)
            {
                foreach (var a in e.Metadata.Aliases)
                    AddLookup(map, a, e);
            }
            AddLookup(map, Path.GetFileName(e.RootPath), e);
        }

        return map;
    }

    private static void AddLookup(Dictionary<string, EntityRecord> map, string? raw, EntityRecord rec)
    {
        var k = NormalizeEntityToken(raw);
        if (string.IsNullOrEmpty(k))
            return;
        if (!map.ContainsKey(k))
            map[k] = rec;
    }

    private static string NormalizeEntityToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var s = raw.Trim().ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static ProjectMemoryPipelineResult Finish(
        string correlationId,
        bool success,
        string finalText,
        IReadOnlyList<ProjectMemoryPipelineStep> steps) =>
        new()
        {
            CorrelationId = correlationId,
            Success = success,
            FinalText = finalText,
            Steps = steps
        };

    private static ProjectMemoryPipelineResult Fail(string correlationId, string message, List<ProjectMemoryPipelineStep> steps)
    {
        steps.Add(new ProjectMemoryPipelineStep { Name = "error", Ok = false, Detail = message });
        return Finish(correlationId, false, message, steps);
    }
}
