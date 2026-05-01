using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Orchestration;

namespace AgctorSDK.Agents.ProjectMemory;

/// <summary>
/// Read-only Q&amp;A over canonical people files using search + LLM synthesis.
/// </summary>
public sealed class PersonQueryProjectAgent : Agent
{
    private readonly IProjectMemoryAgentServices _services;

    public PersonQueryProjectAgent(string id, IProjectMemoryAgentServices? services = null) : base(id)
    {
        _services = services ?? ProjectMemoryAgentServices.Default;
    }

    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not string question)
            return await base.ReceiveAsync(envelope, cancellationToken).ConfigureAwait(false);

        try
        {
            var root = _services.GetProjectRoot();
            if (string.IsNullOrWhiteSpace(root))
                return Env("Configure Agctor:ProjectMemory:ProjectRoot.");

            var ctx = await _services.LoadProjectAsync(root, cancellationToken).ConfigureAwait(false);
            var spec = ctx.AgentSpecs.FirstOrDefault(a => a.Id == "person-query")
                       ?? throw new InvalidOperationException("person-query agent spec missing.");

            var hits = await _services.SearchEntitiesAsync(root, null, cancellationToken).ConfigureAwait(false);
            var sb = new StringBuilder();
            foreach (var h in hits.Take(20))
            {
                var profile = await _services.ReadDocumentAsync(spec, root, $"people/{h.EntityKey}/profile.md", cancellationToken).ConfigureAwait(false);
                sb.AppendLine($"### {h.EntityKey}");
                sb.AppendLine(profile);
                sb.AppendLine();
            }

            var prompt = string.Join('\n', spec.Instructions)
                         + "\nContext:\n" + sb
                         + "\nQuestion:\n" + question;

            return Env((await _services.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false)).Trim());
        }
        catch (Exception ex)
        {
            return Env($"Error: {ex.Message}");
        }
    }

    private static MessageEnvelope Env(string text) =>
        new(text, new Dictionary<string, object> { ["Timestamp"] = DateTimeOffset.UtcNow }, null,
            new Dictionary<string, string> { [AgctorMessageHeaders.SenderId] = "person-query", [AgctorMessageHeaders.MessageType] = "LLMResponse" });
}
