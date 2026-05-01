using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Orchestration;

namespace AgctorSDK.Agents.ProjectMemory;

/// <summary>
/// Calls the local LLM to emit memory intents JSON (read-only file tools via spec; output is JSON text).
/// </summary>
public sealed class PersonExtractorProjectAgent : Agent
{
    private readonly IProjectMemoryAgentServices _services;

    public PersonExtractorProjectAgent(string id, IProjectMemoryAgentServices? services = null) : base(id)
    {
        _services = services ?? ProjectMemoryAgentServices.Default;
    }

    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not string inputText)
            return await base.ReceiveAsync(envelope, cancellationToken).ConfigureAwait(false);

        try
        {
            var root = _services.GetProjectRoot();
            if (string.IsNullOrWhiteSpace(root))
                return Env("Configure Agctor:ProjectMemory:ProjectRoot.");

            var ctx = await _services.LoadProjectAsync(root, cancellationToken).ConfigureAwait(false);
            var spec = ctx.AgentSpecs.FirstOrDefault(a => a.Id == "person-extractor")
                       ?? throw new InvalidOperationException("person-extractor agent spec missing.");

            var prompt = string.Join('\n', spec.Instructions)
                         + "\n\nReturn valid JSON only. Do not wrap JSON in markdown fences."
                         + "\nInput:\n" + inputText;
            var text = (await _services.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false)).Trim();
            return Env(text);
        }
        catch (Exception ex)
        {
            return Env($"Error: {ex.Message}");
        }
    }

    private static MessageEnvelope Env(string text) =>
        new(text, new Dictionary<string, object> { ["Timestamp"] = DateTimeOffset.UtcNow }, null,
            new Dictionary<string, string> { [AgctorMessageHeaders.SenderId] = "person-extractor", [AgctorMessageHeaders.MessageType] = "LLMResponse" });
}
