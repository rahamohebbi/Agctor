using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Agents.ProjectMemory;

/// <summary>
/// Applies validated memory intents to canonical markdown (actor; PRD memory curator).
/// </summary>
public sealed class MemoryCuratorProjectAgent : Agent
{
    public MemoryCuratorProjectAgent(string id) : base(id)
    {
    }

    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not string json)
            return await base.ReceiveAsync(envelope, cancellationToken).ConfigureAwait(false);

        try
        {
            var root = ProjectMemoryServiceAccessor.GetRequiredService<IOptions<ProjectMemoryAgentOptions>>().Value.ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
                return TextEnvelope("Configure Agctor:ProjectMemory:ProjectRoot.");

            var loader = ProjectMemoryServiceAccessor.GetRequiredService<IProjectLoader>();
            var entitiesReg = ProjectMemoryServiceAccessor.GetRequiredService<IEntityRegistry>();
            var processor = ProjectMemoryServiceAccessor.GetRequiredService<IMemoryIntentProcessor>();
            var projection = ProjectMemoryServiceAccessor.GetRequiredService<IDocumentProjectionService>();

            var ctx = await loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            var spec = ctx.AgentSpecs.FirstOrDefault(a => a.Id == "memory-curator")
                       ?? throw new InvalidOperationException("memory-curator agent spec missing.");

            var batch = JsonSerializer.Deserialize<MemoryIntentBatch>(json);
            if (batch?.MemoryIntents == null || batch.MemoryIntents.Count == 0)
                return TextEnvelope("{}");

            var routed = processor.Route(ctx, batch.MemoryIntents, out var routeIssues);
            if (routeIssues.Any(i => i.IsError))
                return TextEnvelope(JsonSerializer.Serialize(new { errors = routeIssues }));

            var discovered = await entitiesReg.DiscoverAsync(ctx, cancellationToken).ConfigureAwait(false);
            var byEntity = routed.GroupBy(r => r.Original.EntityKey, StringComparer.OrdinalIgnoreCase);
            var updated = new List<string>();
            foreach (var g in byEntity)
            {
                var rec = discovered.FirstOrDefault(e => e.EntityKey.Equals(g.Key, StringComparison.OrdinalIgnoreCase));
                if (rec == null)
                    continue;
                var res = await projection.ApplyAsync(rec, g.ToList(), cancellationToken).ConfigureAwait(false);
                updated.AddRange(res.UpdatedFiles);
            }

            return TextEnvelope(JsonSerializer.Serialize(new { updatedFiles = updated, routeWarnings = routeIssues }));
        }
        catch (Exception ex)
        {
            return TextEnvelope($"Error: {ex.Message}");
        }
    }

    private static MessageEnvelope TextEnvelope(string text) =>
        new(text, new Dictionary<string, object> { ["Timestamp"] = DateTimeOffset.UtcNow }, id: null,
            new Dictionary<string, string> { ["SenderId"] = "memory-curator", ["MessageType"] = "LLMResponse" });
}
