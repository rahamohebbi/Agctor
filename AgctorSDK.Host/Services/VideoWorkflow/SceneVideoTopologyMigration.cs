using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>
/// Phase E: map legacy scene-video admission flows (3/4-node AgentLoop graphs) onto topology flags.
/// Historical runs stay as-is; this only seeds the shared topology document when no user overlay exists.
/// </summary>
public static class SceneVideoTopologyMigration
{
    /// <summary>
    /// Returns whether the admission flow enables the Audio Engineer, or null when the flow is not a scene-video loop.
    /// </summary>
    public static bool? DeriveAudioEnabledFromAdmission(ScenarioFlowDocument? flow)
    {
        if (flow?.Nodes == null || !flow.Nodes.Any(SceneVideoScenario.IsLoop))
            return null;
        return flow.Nodes.Any(SceneVideoScenario.IsAudio);
    }

    /// <summary>
    /// When no topology user overlay exists yet, copy audio enable from the admission flow onto the in-memory document
    /// (and optionally persist a one-shot overlay so edit/live views stay aligned after upgrade).
    /// </summary>
    public static bool TrySeedAudioFromAdmission(
        SceneVideoTopologyDocument document,
        ScenarioFlowDocument? admissionFlow,
        out bool audioEnabled)
    {
        audioEnabled = document.AudioEnabled;
        var derived = DeriveAudioEnabledFromAdmission(admissionFlow);
        if (derived == null || derived.Value == document.AudioEnabled)
            return false;
        document.AudioEnabled = derived.Value;
        audioEnabled = derived.Value;
        return true;
    }
}
