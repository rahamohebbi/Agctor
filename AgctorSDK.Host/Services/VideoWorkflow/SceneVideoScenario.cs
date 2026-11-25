using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed class SceneVideoScenario(
    IScenarioCatalog catalog,
    ISceneMemoryStore memory,
    IAgentRunStore runs,
    ISceneVideoTopologyCatalog topology)
{
    /// <summary>Flow-editor role slug → live mailbox id (matches SceneVideoRoles).</summary>
    public static readonly IReadOnlyDictionary<string, string> RoleMailboxes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["director"] = SceneVideoRoles.Director,
            ["planner"] = SceneVideoRoles.Planner,
            ["project"] = SceneVideoRoles.Project,
            ["memory"] = SceneVideoRoles.Memory,
            ["scene-memory"] = SceneVideoRoles.Memory,
            ["render"] = SceneVideoRoles.Render,
            ["visual-critic"] = SceneVideoRoles.VisualCritic,
            ["critic"] = SceneVideoRoles.VisualCritic,
            ["editor"] = SceneVideoRoles.Editor,
            ["audio-engineer"] = SceneVideoRoles.AudioEngineer,
            ["audio"] = SceneVideoRoles.AudioEngineer,
            ["dialogue-supervisor"] = SceneVideoRoles.DialogueSupervisor,
            ["dialogue"] = SceneVideoRoles.DialogueSupervisor,
            ["voice-actor"] = SceneVideoRoles.VoiceActor,
            ["voice"] = SceneVideoRoles.VoiceActor
        };

    public static string? RoleSlug(ScenarioFlowNode node)
    {
        if (!node.Type.Equals("AgentLoop", StringComparison.OrdinalIgnoreCase)) return null;
        if (node.Config is not { ValueKind: JsonValueKind.Object } c) return null;
        if (!c.TryGetProperty("role", out var r) || r.ValueKind != JsonValueKind.String) return null;
        var slug = r.GetString();
        return string.IsNullOrWhiteSpace(slug) ? null : slug.Trim();
    }

    public static bool IsCapability(ScenarioFlowNode node) =>
        node.Type.Equals("Capability", StringComparison.OrdinalIgnoreCase);

    public static bool IsAudio(ScenarioFlowNode node) =>
        string.Equals(RoleSlug(node), "audio-engineer", StringComparison.OrdinalIgnoreCase)
        || string.Equals(RoleSlug(node), "audio", StringComparison.OrdinalIgnoreCase);

    /// <summary>Director hub AgentLoop (no specialist role, or role=director).</summary>
    public static bool IsDirector(ScenarioFlowNode node)
    {
        if (!node.Type.Equals("AgentLoop", StringComparison.OrdinalIgnoreCase)) return false;
        var slug = RoleSlug(node);
        return slug == null || slug.Equals("director", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Legacy name: non-audio AgentLoop. Prefer <see cref="IsDirector"/> for the hub.</summary>
    public static bool IsLoop(ScenarioFlowNode node) => IsDirector(node);

    public static bool IsSpecialist(ScenarioFlowNode node)
    {
        if (!node.Type.Equals("AgentLoop", StringComparison.OrdinalIgnoreCase)) return false;
        var slug = RoleSlug(node);
        if (slug == null || slug.Equals("director", StringComparison.OrdinalIgnoreCase)) return false;
        return RoleMailboxes.ContainsKey(slug);
    }

    public static string? MailboxFor(ScenarioFlowNode node)
    {
        if (IsDirector(node)) return SceneVideoRoles.Director;
        var slug = RoleSlug(node);
        if (slug != null && RoleMailboxes.TryGetValue(slug, out var mailbox)) return mailbox;
        return null;
    }

    public static string? Validate(ScenarioFlowDocument flow)
    {
        if (!flow.Nodes.Any(n => n.Type.Equals("AgentLoop", StringComparison.OrdinalIgnoreCase))) return null;
        var input = flow.Nodes.FirstOrDefault(n => n.Type == "ChatInput");
        var output = flow.Nodes.FirstOrDefault(n => n.Type == "Output");
        var directors = flow.Nodes.Where(IsDirector).ToArray();
        var specialists = flow.Nodes.Where(IsSpecialist).ToArray();
        var audio = flow.Nodes.Where(IsAudio).ToArray();
        var capabilities = flow.Nodes.Where(IsCapability).ToArray();
        if (input == null || output == null)
            return "Scene-video flow needs ChatInput and Output.";
        if (directors.Length != 1)
            return "Scene-video flow needs exactly one Director AgentLoop.";
        var loop = directors[0];
        foreach (var agent in specialists.Concat(directors))
        {
            if (agent.Config is not { ValueKind: JsonValueKind.Object } cfg ||
                !cfg.TryGetProperty("workflow", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "scene-video")
                return $"AgentLoop '{agent.Id}' requires config.workflow = scene-video.";
        }
        if (audio.Length > 1)
            return "At most one Audio Engineer AgentLoop is allowed.";
        if (audio.Length == 1 && (!flow.Edges.Any(e => e.FromNodeId == loop.Id && e.ToNodeId == audio[0].Id) ||
                                  !flow.Edges.Any(e => e.FromNodeId == audio[0].Id && e.ToNodeId == loop.Id)))
            return "Audio Engineer requires both a request from and a report back to the Director.";
        if (!flow.Edges.Any(e => e.FromNodeId == input.Id && e.ToNodeId == loop.Id) ||
            !flow.Edges.Any(e => e.FromNodeId == loop.Id && e.ToNodeId == output.Id))
            return "AgentLoop edges must connect ChatInput → Director → Output.";
        // Specialists talk with Director both ways when present.
        foreach (var specialist in specialists.Where(s => !IsAudio(s)))
        {
            if (!flow.Edges.Any(e => e.FromNodeId == loop.Id && e.ToNodeId == specialist.Id) ||
                !flow.Edges.Any(e => e.FromNodeId == specialist.Id && e.ToNodeId == loop.Id))
                return $"Specialist '{specialist.Label ?? specialist.Id}' needs Director ↔ specialist edges.";
        }
        if (flow.Edges.Any(e => e.Mode != "sequential" || !string.IsNullOrEmpty(e.Condition)))
            return "Scene-video edges must be sequential without conditions.";
        if (loop.Config is not { ValueKind: JsonValueKind.Object } config)
            return "Director AgentLoop requires config.";
        if (config.TryGetProperty("maxAttempts", out var attempts) && (attempts.ValueKind != JsonValueKind.Number || !attempts.TryGetInt32(out var count) || count is < 1 or > 5))
            return "AgentLoop maxAttempts must be between 1 and 5.";
        if (config.TryGetProperty("instructions", out var instructions) && (instructions.ValueKind != JsonValueKind.String || instructions.GetString()!.Length > 4000))
            return "AgentLoop instructions must be text up to 4,000 characters.";
        foreach (var cap in capabilities)
        {
            if (cap.Config is not { ValueKind: JsonValueKind.Object } capCfg ||
                !capCfg.TryGetProperty("kind", out var capKind) || capKind.ValueKind != JsonValueKind.String ||
                capKind.GetString() is not ("tool" or "llm" or "memory"))
                return $"Capability '{cap.Id}' needs config.kind of tool, llm, or memory.";
        }
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
        // Replace-at-slot is only for continue; ContinuityMedia chooses final mix vs original previous clip.
        if (input.ReplaceClipIndex != null && (mode != "continue" || input.ReplaceClipIndex is < 1 or > 24))
            throw new ArgumentException("Replace clip index is a 1–24 film slot used only when continuing a story.");
        if (input.ContinuityMedia is not (null or "final" or "original"))
            throw new ArgumentException("Continuity media must be final (New mix when present) or original.");
        // Keep-later only makes sense with a replace slot; omit/false keeps the trim-from-N path.
        if (input.KeepLaterClips != null && input.ReplaceClipIndex == null)
            throw new ArgumentException("Keep later clips requires a replace clip index.");
        if (string.IsNullOrWhiteSpace(input.RequestId) || input.RequestId.Length > 128 ||
            input.WorkspaceId?.Length > 100 || string.IsNullOrWhiteSpace(input.Prompt) || input.Prompt.Length > 12000 ||
            input.TotalSeconds is < 1 or > 360 || input.ClipSeconds is < 1 or > 15 ||
            Math.Ceiling((double)input.TotalSeconds / input.ClipSeconds) > 24 || input.Model?.Length > 200)
            throw new ArgumentException("Provide a request ID, prompt, 1–360 total seconds, and 1–15 seconds per clip (maximum 24 clips).");
        var scenario = catalog.Get(scenarioId) ?? throw new KeyNotFoundException("Scenario not found.");
        if (scenario.Flow == null || !scenario.Flow.Nodes.Any(IsDirector)) throw new ArgumentException("This scenario has no AgentLoop workflow.");
        var errors = ScenarioFlowValidator.Validate(scenario);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        var workspace = mode == "continue" ? await memory.GetAsync(input.WorkspaceId!, ct) : null;
        var config = scenario.Flow.Nodes.Single(IsDirector).Config!.Value;
        // Topology.audioEnabled is the product flag; flow Audio Engineer is the fallback.
        var topo = topology.GetForScenario(scenarioId);
        var audioEnabled = topo?.AudioEnabled ?? scenario.Flow.Nodes.Any(IsAudio);
        if (input.NarrationMode == "dedicated" && !audioEnabled) throw new ArgumentException("Dedicated narration requires an Audio Engineer.");
        var state = new SceneVideoState { SpeechReviewEnabled = audioEnabled && (input.SpeechLanguage != null || input.NarrationMode == "dedicated"),
            ExpectedSpeechLanguage = input.SpeechLanguage ?? "auto", ProductionEnabled = true, AudioEnabled = audioEnabled, Input = input with { StoryMode = mode }, Model = string.IsNullOrWhiteSpace(input.Model)
            ? scenario.ModelDefaults.Generation ?? OllamaRuntimeConfiguration.GetDefaultModel() : input.Model.Trim(),
            WorkspaceRevision = workspace == null ? "" : ScenePromptReducer.Revision(workspace), StoryId = workspace?.Connection?.StoryId,
            StoryName = workspace?.Name ?? System.Text.RegularExpressions.Regex.Replace(input.Prompt.Trim(), @"\s+", " ").Truncate(80),
            MaxAttempts = config.TryGetProperty("maxAttempts", out var attempts) ? attempts.GetInt32() : 3,
            Instructions = config.TryGetProperty("instructions", out var guidance) ? guidance.GetString()! : "" };
        // Preserve existing-client hashes for requests that specified a workspace before story modes existed.
        var serialized = input.StoryMode == null && mode == "continue" && input.AudioModel == null && input.SpeechLanguage == null && input.ProjectId == null && input.NarrationMode == null
            && input.ReplaceClipIndex == null && input.ContinuityMedia == null && input.KeepLaterClips == null
            ? JsonSerializer.Serialize(new { scenarioId, input = new { input.RequestId, input.WorkspaceId, input.Prompt, input.TotalSeconds, input.ClipSeconds, input.Model, input.ReviewPlan } })
            : JsonSerializer.Serialize(new { scenarioId, input });
        return await runs.CreateAsync(new AgentRun { RequestId = input.RequestId, ScenarioId = scenarioId,
            RequestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))),
            WorkspaceId = workspace?.Id ?? "pending-" + Guid.NewGuid().ToString("N"), Workflow = "scene-video",
            Phase = workspace == null ? "initialize" : "project", State = JsonSerializer.SerializeToElement(state),
            Summary = workspace == null ? "Queued. Creating a new story with fresh Scene Memory." : "Queued. Continuing the selected story." }, ct);
    }
}
