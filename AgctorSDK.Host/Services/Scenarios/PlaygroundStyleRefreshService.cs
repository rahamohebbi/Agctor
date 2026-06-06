using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Host.Services.ProjectMemory;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Optional style-coach refresh after generic inbox approval (PRD-024 photo loop UX).
/// </summary>
public sealed class PlaygroundStyleRefreshService
{
    private const string StylePhotoLoopScenarioId = "people-style-photo-loop";

    private readonly IScenarioCatalog _catalog;
    private readonly IScenarioFlowRuntimeStore _flowStore;
    private readonly IScenarioFlowPersonaLlmRunner _flowPersonaRunner;
    private readonly ILogger<PlaygroundStyleRefreshService> _logger;

    public PlaygroundStyleRefreshService(
        IScenarioCatalog catalog,
        IScenarioFlowRuntimeStore flowStore,
        IScenarioFlowPersonaLlmRunner flowPersonaRunner,
        ILogger<PlaygroundStyleRefreshService> logger)
    {
        _catalog = catalog;
        _flowStore = flowStore;
        _flowPersonaRunner = flowPersonaRunner;
        _logger = logger;
    }

    /// <summary>Runs style coach once after the inbox batch is fully reviewed (no pending rows left).</summary>
    public async Task<string?> TryRefreshStyleAdviceAsync(
        string projectRoot,
        string scenarioId,
        string? sessionId,
        int approvedCount,
        int pendingRemaining,
        CancellationToken cancellationToken = default)
    {
        if (approvedCount <= 0
            || pendingRemaining > 0
            || string.IsNullOrWhiteSpace(sessionId)
            || !string.Equals(scenarioId.Trim(), StylePhotoLoopScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var def = _catalog.Get(scenarioId.Trim());
        if (def?.Flow == null)
            return null;

        var flowJson = JsonSerializer.Serialize(def.Flow, ScenarioFlowJson.Options);
        var snapshot = await _flowStore
            .LoadAsync(projectRoot, sessionId.Trim(), scenarioId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        var userMessage = ScenarioFlowRuntimePrompts.BuildInboxRefreshStyleUserMessage(snapshot, flowJson);
        var sessionAssets = snapshot?.Store.Attachments.AllInRun;
        var result = await _flowPersonaRunner
            .RunFlowNodeAsync(
                new ScenarioFlowPersonaRunRequest
                {
                    ProjectRoot = projectRoot,
                    ScenarioId = scenarioId.Trim(),
                    SessionId = sessionId.Trim(),
                    AgentId = "style-coach",
                    InputText = userMessage,
                    FlowNodeId = "n_style",
                    Snapshot = snapshot,
                    RelaxVisualEntityFilter = true,
                    SessionAssetIds = sessionAssets is { Count: > 0 } ? sessionAssets : null
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Ok || string.IsNullOrWhiteSpace(result.OutputText))
        {
            _logger.LogDebug(
                "Style refresh after inbox skipped: {Error}",
                result.ErrorMessage ?? "empty output");
            return null;
        }

        return result.OutputText.Trim();
    }
}
