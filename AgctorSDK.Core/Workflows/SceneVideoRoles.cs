namespace AgctorSDK.Core.Workflows;

/// <summary>
/// Phase C role boundaries: every scene-video agent has a mailbox and exclusive capabilities.
/// Director only dispatches / asks Human / applies pure decisions — never specialist tools.
/// </summary>
public static class SceneVideoRoles
{
    public const string Director = "Director";
    public const string AudioEngineer = "Audio Engineer";
    public const string Planner = "Planner";
    public const string Project = "Project agent";
    public const string Memory = "Scene Memory agent";
    public const string Render = "Render agent";
    public const string VisualCritic = "Visual critic";
    public const string Editor = "Editor agent";

    /// <summary>Stable display/mailbox names for the fixed roster (run-scoped actors).</summary>
    public static readonly string[] Agents =
    [
        Director, AudioEngineer, Planner, Project, Memory, Render, VisualCritic, Editor
    ];

    public static readonly string[] DirectorTools = [];
    public static readonly string[] AudioEngineerTools = ["Audio production", "Speech inspection", "Ollama"];
    public static readonly string[] PlannerTools = ["Ollama", "Scene Memory"];
    public static readonly string[] ProjectTools = ["Project storage"];
    public static readonly string[] MemoryTools = ["Scene Memory", "Story storage"];
    public static readonly string[] RenderTools = ["Video rendering", "Frame sampler"];
    public static readonly string[] VisualCriticTools = ["Ollama", "Frame sampler"];
    public static readonly string[] EditorTools = ["Film assembly"];

    /// <summary>Phases executed only inside the Audio Engineer mailbox.</summary>
    public static bool OwnsAudioWork(string phase) => phase is
        "narration-plan" or "speech-intent" or "speech-check" or
        "audio-plan" or "audio-process" or "audio-review" or
        "clip-audio-plan" or "clip-audio-process" or "clip-audio-check";

    /// <summary>Director asks Human for the audio choice (no tool HTTP).</summary>
    public static bool OwnsDirectorAudioAsk(string phase) => phase is
        "audio-decision" or "clip-audio-decision";

    public static bool NeedsAudioWaitingObserver(AgentRun run) =>
        run.Status == "waiting" && run.Reply == null && run.Question?.Kind == "continuity";

    public static bool NeedsRenderWaitingObserver(AgentRun run) =>
        run.Status == "waiting" && run.Reply == null &&
        run.Question?.Kind is "timeout" or "retry" && run.Phase is "poll" or "submit";

    public static bool NeedsPlannerWaitingObserver(AgentRun run) =>
        run.Status == "waiting" && run.Reply == null &&
        run.Phase == "plan-review" && run.Question?.Kind is "retry" or "planning-retry";

    /// <summary>
    /// Mailbox that owns this tick. Human replies that need specialist tools go to that specialist;
    /// pure Director decisions (incl. audio choice) stay on Director — no Role() hat-switching.
    /// </summary>
    public static string Owner(AgentRun run)
    {
        if (run.Reply != null)
        {
            return run.Question?.Kind switch
            {
                "continuity" or "boundary" => VisualCritic,
                "planning-retry" => Planner,
                "planning" or "clarification" or "review" => Planner,
                _ => Director // audio decisions, render continue/timeout replies, cancel, conflict, …
            };
        }
        if (NeedsAudioWaitingObserver(run)) return AudioEngineer;
        if (NeedsRenderWaitingObserver(run)) return Render;
        if (NeedsPlannerWaitingObserver(run)) return Planner;
        return run.Phase switch
        {
            _ when OwnsAudioWork(run.Phase) => AudioEngineer,
            _ when OwnsDirectorAudioAsk(run.Phase) => Director,
            "plan" or "plan-review" => Planner,
            "project" => Project,
            "critic" => VisualCritic,
            "finish" => Editor,
            "prepare" or "commit" or "initialize" => Memory,
            "submit" or "poll" or "reset-render" => Render,
            _ => Director
        };
    }

    public static IReadOnlyList<string> ToolsFor(string role) => role switch
    {
        Director => DirectorTools,
        AudioEngineer => AudioEngineerTools,
        Planner => PlannerTools,
        Project => ProjectTools,
        Memory => MemoryTools,
        Render => RenderTools,
        VisualCritic => VisualCriticTools,
        Editor => EditorTools,
        _ => []
    };

    /// <summary>Map live-graph targets (incl. Ollama · model) to capability ids used in the matrix.</summary>
    public static string CanonicalCapability(string target, string kind)
    {
        if (kind == "llm" || target.StartsWith("Ollama", StringComparison.Ordinal)) return "Ollama";
        if (kind == "memory" || target == "Scene Memory") return "Scene Memory";
        return target;
    }

    public static bool IsAudioTool(string tool) =>
        tool is "Audio production" or "Speech inspection";

    /// <summary>True when this role may invoke the named capability (tool / llm / memory).</summary>
    public static bool MayInvoke(string role, string tool)
    {
        var capability = CanonicalCapability(tool, tool.StartsWith("Ollama", StringComparison.Ordinal) ? "llm" : "tool");
        var allowed = ToolsFor(role);
        return allowed.Contains(capability, StringComparer.Ordinal);
    }
}
