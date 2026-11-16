namespace AgctorSDK.Core.Workflows;

/// <summary>
/// Phase B role boundaries for scene-video: exclusive tools and which phases each mailbox owns.
/// Other specialists stay label-based until Phase C.
/// </summary>
public static class SceneVideoRoles
{
    public const string Director = "Director";
    public const string AudioEngineer = "Audio Engineer";

    /// <summary>Cynelo tools only the Audio Engineer may invoke.</summary>
    public static readonly string[] AudioEngineerTools =
        ["Audio production", "Speech inspection"];

    /// <summary>Director routes / asks Human / applies audio decisions — no Cynelo tools.</summary>
    public static readonly string[] DirectorTools = [];

    /// <summary>Phases executed only inside the Audio Engineer mailbox.</summary>
    public static bool OwnsAudioWork(string phase) => phase is
        "narration-plan" or "speech-intent" or "speech-check" or
        "audio-plan" or "audio-process" or "audio-review" or
        "clip-audio-plan" or "clip-audio-process" or "clip-audio-check";

    /// <summary>Director asks Human for the audio choice (no tool HTTP).</summary>
    public static bool OwnsDirectorAudioAsk(string phase) => phase is
        "audio-decision" or "clip-audio-decision";

    /// <summary>Waiting continuity observations may inspect speech — Audio Engineer work.</summary>
    public static bool NeedsAudioWaitingObserver(AgentRun run) =>
        run.Status == "waiting" && run.Reply == null && run.Question?.Kind == "continuity";

    public static bool IsAudioTool(string tool) =>
        tool is "Audio production" or "Speech inspection";

    /// <summary>True when this role may invoke the named capability in the live graph.</summary>
    public static bool MayInvoke(string role, string tool)
    {
        if (string.Equals(role, Director, StringComparison.Ordinal))
            return false;
        if (IsAudioTool(tool))
            return string.Equals(role, AudioEngineer, StringComparison.Ordinal);
        if (string.Equals(role, AudioEngineer, StringComparison.Ordinal))
            return AudioEngineerTools.Contains(tool, StringComparer.Ordinal);
        // Phase C will tighten remaining specialists' tool sets.
        return true;
    }
}
