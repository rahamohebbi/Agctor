using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Agents.ProjectMemory;

/// <summary>
/// Calls the local LLM to emit memory intents JSON (read-only file tools via spec; output is JSON text).
/// </summary>
public sealed class PersonExtractorProjectAgent : Agent
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public PersonExtractorProjectAgent(string id) : base(id)
    {
    }

    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not string inputText)
            return await base.ReceiveAsync(envelope, cancellationToken).ConfigureAwait(false);

        try
        {
            var root = ProjectMemoryServiceAccessor.GetRequiredService<IOptions<ProjectMemoryAgentOptions>>().Value.ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
                return Env("Configure Agctor:ProjectMemory:ProjectRoot.");

            var loader = ProjectMemoryServiceAccessor.GetRequiredService<IProjectLoader>();
            var ctx = await loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            var spec = ctx.AgentSpecs.FirstOrDefault(a => a.Id == "person-extractor")
                       ?? throw new InvalidOperationException("person-extractor agent spec missing.");

            var sys = string.Join('\n', spec.Instructions)
                      + "\n\nRespond with ONLY valid JSON: {\"memoryIntents\":[{\"entityKey\":\"\",\"knowledgeType\":\"\",\"attribute\":\"\",\"value\":\"\",\"confidence\":0.9}]}\n";

            var ollama = LLMAgent.GetConfiguredOllamaApiUrl().TrimEnd('/') + "/";
            var model = LLMAgent.GetConfiguredDefaultModel();
            var prompt = sys + "\nInput:\n" + inputText;

            var req = new { model, prompt, stream = false };
            var resp = await Http.PostAsJsonAsync(ollama + "api/generate", req, cancellationToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<OllamaGen>(cancellationToken: cancellationToken).ConfigureAwait(false);
            var text = doc?.response?.Trim() ?? "";
            return Env(text);
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
            new Dictionary<string, string> { ["SenderId"] = "person-extractor", ["MessageType"] = "LLMResponse" });
}
