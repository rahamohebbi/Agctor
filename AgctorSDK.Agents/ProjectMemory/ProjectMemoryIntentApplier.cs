using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Agents.ProjectMemory;

/// <summary>
/// Deterministic apply of extractor JSON (<see cref="MemoryIntentBatch"/>) to markdown — same pipeline as
/// <see cref="MemoryCuratorProjectAgent"/>, callable from tools without duplicating orchestration rules.
/// </summary>
public static class ProjectMemoryIntentApplier
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Applies intents; returns JSON text body suitable for LLM/tool consumers.</summary>
    public static async Task<string> ApplyFromJsonAsync(
        string json,
        IProjectMemoryAgentServices services,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "{}";

        var root = services.GetProjectRoot();
        if (string.IsNullOrWhiteSpace(root))
            return JsonSerializer.Serialize(new { error = "Configure Agctor:ProjectMemory:ProjectRoot." });

        LoadedProjectContext ctx;
        try
        {
            ctx = await services.LoadProjectAsync(root, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }

        MemoryIntentBatch? batch;
        try
        {
            batch = JsonSerializer.Deserialize<MemoryIntentBatch>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Invalid JSON: {ex.Message}" });
        }

        if (batch?.MemoryIntents == null || batch.MemoryIntents.Count == 0)
            return "{}";

        try
        {
            var routed = services.Route(ctx, batch.MemoryIntents, out var routeIssues);
            if (routeIssues.Any(i => i.IsError))
                return JsonSerializer.Serialize(new { errors = routeIssues });

            var scenarioId = string.IsNullOrWhiteSpace(batch.ScenarioId) ? null : batch.ScenarioId.Trim();
            var entityWorkspace = PersonaScenarioScope.GetEntityWorkspaceRoot(root, scenarioId);
            if (!PersonaScenarioScope.IsUnderProjectRoot(root, entityWorkspace))
                return JsonSerializer.Serialize(new { error = "Invalid scenario scope." });

            var discovered = await services.DiscoverAsync(ctx, entityWorkspace, cancellationToken).ConfigureAwait(false);
            var byEntity = routed.GroupBy(r => r.Original.EntityKey, StringComparer.OrdinalIgnoreCase);
            var updated = new List<string>();
            foreach (var g in byEntity)
            {
                var rec = discovered.FirstOrDefault(e => e.EntityKey.Equals(g.Key, StringComparison.OrdinalIgnoreCase));
                if (rec == null)
                    continue;
                var res = await services.ApplyProjectionAsync(rec, g.ToList(), cancellationToken).ConfigureAwait(false);
                updated.AddRange(res.UpdatedFiles);
            }

            return JsonSerializer.Serialize(new { updatedFiles = updated, routeWarnings = routeIssues });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
