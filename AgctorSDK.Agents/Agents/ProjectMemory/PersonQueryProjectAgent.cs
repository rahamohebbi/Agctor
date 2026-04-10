using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Tools;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Agents.ProjectMemory;

/// <summary>
/// Read-only Q&amp;A over canonical people files using search + LLM synthesis.
/// </summary>
public sealed class PersonQueryProjectAgent : Agent
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public PersonQueryProjectAgent(string id) : base(id)
    {
    }

    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not string question)
            return await base.ReceiveAsync(envelope, cancellationToken).ConfigureAwait(false);

        try
        {
            var root = ProjectMemoryServiceAccessor.GetRequiredService<IOptions<ProjectMemoryAgentOptions>>().Value.ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
                return Env("Configure Agctor:ProjectMemory:ProjectRoot.");

            var loader = ProjectMemoryServiceAccessor.GetRequiredService<IProjectLoader>();
            var ops = ProjectMemoryServiceAccessor.GetRequiredService<ProjectMemoryOperations>();
            var ctx = await loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            var spec = ctx.AgentSpecs.FirstOrDefault(a => a.Id == "person-query")
                       ?? throw new InvalidOperationException("person-query agent spec missing.");

            var hits = await ops.SearchEntitiesAsync(root, null, cancellationToken).ConfigureAwait(false);
            var sb = new StringBuilder();
            foreach (var h in hits.Take(20))
            {
                var profile = await ops.ReadDocumentAsync(spec, root, $"people/{h.EntityKey}/profile.md", cancellationToken).ConfigureAwait(false);
                sb.AppendLine($"### {h.EntityKey}");
                sb.AppendLine(profile);
                sb.AppendLine();
            }

            var ollama = LLMAgent.GetConfiguredOllamaApiUrl().TrimEnd('/') + "/";
            var model = LLMAgent.GetConfiguredDefaultModel();
            var prompt = string.Join('\n', spec.Instructions)
                         + "\nContext:\n" + sb
                         + "\nQuestion:\n" + question;

            var req = new { model, prompt, stream = false };
            var resp = await Http.PostAsJsonAsync(ollama + "api/generate", req, cancellationToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<OllamaGen>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return Env(doc?.response?.Trim() ?? "");
        }
        catch (Exception ex)
        {
            return Env($"Error: {ex.Message}");
        }
    }

    private sealed class OllamaGen
    {
        public string? response { get; set; }
    }

    private static MessageEnvelope Env(string text) =>
        new(text, new Dictionary<string, object> { ["Timestamp"] = DateTimeOffset.UtcNow }, null,
            new Dictionary<string, string> { ["SenderId"] = "person-query", ["MessageType"] = "LLMResponse" });
}
