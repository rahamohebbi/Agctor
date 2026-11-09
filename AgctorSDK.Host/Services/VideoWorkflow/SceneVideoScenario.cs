using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed class SceneVideoScenario(IScenarioCatalog catalog, ISceneMemoryStore memory, IAgentRunStore runs)
{
    public static bool IsLoop(ScenarioFlowNode node) => node.Type.Equals("AgentLoop", StringComparison.OrdinalIgnoreCase) && !IsAudio(node);
    public static bool IsAudio(ScenarioFlowNode node) => node.Type == "AgentLoop" && node.Config is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() == "audio-engineer";
    public static string? Validate(ScenarioFlowDocument flow)
    {
        if (!flow.Nodes.Any(IsLoop)) return null;
        var input = flow.Nodes.FirstOrDefault(n => n.Type == "ChatInput");
        var output = flow.Nodes.FirstOrDefault(n => n.Type == "Output");
        var loops = flow.Nodes.Where(IsLoop).ToArray();
        var audio = flow.Nodes.Where(IsAudio).ToArray();
        if (audio.Length > 1 || flow.Nodes.Count != 3 + audio.Length || loops.Length != 1 || input == null || output == null || flow.Edges.Count != 2 + audio.Length * 2)
            return "Use Input → Director → Output, optionally with Director ↔ Audio Engineer.";
        var loop = loops[0];
        if (audio.Length == 1 && (!audio[0].Config!.Value.TryGetProperty("workflow", out var audioWorkflow) || audioWorkflow.ValueKind != JsonValueKind.String || audioWorkflow.GetString() != "scene-video"))
            return "Audio Engineer requires config.workflow = scene-video.";
        if (audio.Length == 1 && (!flow.Edges.Any(e => e.FromNodeId == loop.Id && e.ToNodeId == audio[0].Id) || !flow.Edges.Any(e => e.FromNodeId == audio[0].Id && e.ToNodeId == loop.Id)))
            return "Audio Engineer requires both a request from and a report back to the Director.";
        if (!flow.Edges.Any(e => e.FromNodeId == input.Id && e.ToNodeId == loop.Id) ||
            !flow.Edges.Any(e => e.FromNodeId == loop.Id && e.ToNodeId == output.Id) ||
            flow.Edges.Any(e => e.Mode != "sequential" || !string.IsNullOrEmpty(e.Condition)))
            return "AgentLoop edges must connect ChatInput → AgentLoop → Output sequentially without conditions.";
        if (loop.Config is not { ValueKind: JsonValueKind.Object } config ||
            !config.TryGetProperty("workflow", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "scene-video")
            return "AgentLoop requires config.workflow = scene-video.";
        if (config.TryGetProperty("maxAttempts", out var attempts) && (attempts.ValueKind != JsonValueKind.Number || !attempts.TryGetInt32(out var count) || count is < 1 or > 5))
            return "AgentLoop maxAttempts must be between 1 and 5.";
        if (config.TryGetProperty("instructions", out var instructions) && (instructions.ValueKind != JsonValueKind.String || instructions.GetString()!.Length > 4000))
            return "AgentLoop instructions must be text up to 4,000 characters.";
        return null;
    }

    public async Task<AgentRun> StartAsync(string scenarioId, StartSceneVideoRun input, CancellationToken ct)
    {
        if (input.NarrationMode is not (null or "native" or "dedicated") || (input.ProjectId != null && !Guid.TryParseExact(input.ProjectId, "N", out _)) || input.AudioModel is not (null or "roformer" or "demucs") ||
            (input.SpeechLanguage != null && input.SpeechLanguage is not ("auto" or "none") &&
             !(input.SpeechLanguage.Length == 2 && input.SpeechLanguage.All(char.IsAsciiLetterLower))))
            throw new ArgumentException("Choose an available audio model and spoken language.");
        var mode = input.StoryMode ?? (string.IsNullOrWhiteSpace(input.WorkspaceId) ? "new" : "continue");
        if (mode is not ("new" or "continue") || (mode == "new" && !string.IsNullOrWhiteSpace(input.WorkspaceId)) ||
            (mode == "continue" && string.IsNullOrWhiteSpace(input.WorkspaceId)))
            throw new ArgumentException("Choose New story without a workspace, or Continue an existing story with a workspace.");
        if (string.IsNullOrWhiteSpace(input.RequestId) || input.RequestId.Length > 128 ||
            input.WorkspaceId?.Length > 100 || string.IsNullOrWhiteSpace(input.Prompt) || input.Prompt.Length > 12000 ||
            input.TotalSeconds is < 1 or > 360 || input.ClipSeconds is < 1 or > 15 ||
            Math.Ceiling((double)input.TotalSeconds / input.ClipSeconds) > 24 || input.Model?.Length > 200)
            throw new ArgumentException("Provide a request ID, prompt, 1–360 total seconds, and 1–15 seconds per clip (maximum 24 clips).");
        var scenario = catalog.Get(scenarioId) ?? throw new KeyNotFoundException("Scenario not found.");
        if (scenario.Flow == null || !scenario.Flow.Nodes.Any(IsLoop)) throw new ArgumentException("This scenario has no AgentLoop workflow.");
        var errors = ScenarioFlowValidator.Validate(scenario);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        var workspace = mode == "continue" ? await memory.GetAsync(input.WorkspaceId!, ct) : null;
        var config = scenario.Flow.Nodes.Single(IsLoop).Config!.Value;
        if (input.NarrationMode == "dedicated" && !scenario.Flow.Nodes.Any(IsAudio)) throw new ArgumentException("Dedicated narration requires an Audio Engineer.");
        var state = new SceneVideoState { SpeechReviewEnabled = scenario.Flow.Nodes.Any(IsAudio) && (input.SpeechLanguage != null || input.NarrationMode == "dedicated"),
            ExpectedSpeechLanguage = input.SpeechLanguage ?? "auto", ProductionEnabled = true, AudioEnabled = scenario.Flow.Nodes.Any(IsAudio), Input = input with { StoryMode = mode }, Model = string.IsNullOrWhiteSpace(input.Model)
            ? scenario.ModelDefaults.Generation ?? OllamaRuntimeConfiguration.GetDefaultModel() : input.Model.Trim(),
            WorkspaceRevision = workspace == null ? "" : ScenePromptReducer.Revision(workspace), StoryId = workspace?.Connection?.StoryId,
            StoryName = workspace?.Name ?? System.Text.RegularExpressions.Regex.Replace(input.Prompt.Trim(), @"\s+", " ").Truncate(80),
            MaxAttempts = config.TryGetProperty("maxAttempts", out var attempts) ? attempts.GetInt32() : 3,
            Instructions = config.TryGetProperty("instructions", out var guidance) ? guidance.GetString()! : "" };
        // Preserve existing-client hashes for requests that specified a workspace before story modes existed.
        var serialized = input.StoryMode == null && mode == "continue" && input.AudioModel == null && input.SpeechLanguage == null && input.ProjectId == null && input.NarrationMode == null
            ? JsonSerializer.Serialize(new { scenarioId, input = new { input.RequestId, input.WorkspaceId, input.Prompt, input.TotalSeconds, input.ClipSeconds, input.Model, input.ReviewPlan } })
            : JsonSerializer.Serialize(new { scenarioId, input });
        return await runs.CreateAsync(new AgentRun { RequestId = input.RequestId, ScenarioId = scenarioId,
            RequestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))),
            WorkspaceId = workspace?.Id ?? "pending-" + Guid.NewGuid().ToString("N"), Workflow = "scene-video",
            Phase = workspace == null ? "initialize" : "project", State = JsonSerializer.SerializeToElement(state),
            Summary = workspace == null ? "Queued. Creating a new story with fresh Scene Memory." : "Queued. Continuing the selected story." }, ct);
    }
}
