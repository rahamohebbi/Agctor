using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;

namespace AgctorSDK.Agents.ProjectMemory;

/// <summary>
/// Applies validated memory intents to canonical markdown (actor; PRD memory curator).
/// </summary>
public sealed class MemoryCuratorProjectAgent : Agent
{
    private readonly IProjectMemoryAgentServices _services;

    public MemoryCuratorProjectAgent(string id, IProjectMemoryAgentServices? services = null) : base(id)
    {
        _services = services ?? ProjectMemoryAgentServices.Default;
    }

    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not string json)
            return await base.ReceiveAsync(envelope, cancellationToken).ConfigureAwait(false);

        try
        {
            var root = _services.GetProjectRoot();
            if (string.IsNullOrWhiteSpace(root))
                return TextEnvelope("Configure Agctor:ProjectMemory:ProjectRoot.");

            var ctx = await _services.LoadProjectAsync(root, cancellationToken).ConfigureAwait(false);
            _ = ctx.AgentSpecs.FirstOrDefault(a => a.Id == "memory-curator")
                ?? throw new InvalidOperationException("memory-curator agent spec missing.");

            var body = await ProjectMemoryIntentApplier.ApplyFromJsonAsync(json, _services, cancellationToken).ConfigureAwait(false);
            return TextEnvelope(body);
        }
        catch (Exception ex)
        {
            return TextEnvelope($"Error: {ex.Message}");
        }
    }

    private static MessageEnvelope TextEnvelope(string text) =>
        new(text, new Dictionary<string, object> { ["Timestamp"] = DateTimeOffset.UtcNow }, id: null,
            new Dictionary<string, string> { [AgctorMessageHeaders.SenderId] = "memory-curator", [AgctorMessageHeaders.MessageType] = "LLMResponse" });
}
