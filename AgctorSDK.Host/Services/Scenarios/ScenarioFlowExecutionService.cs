using System.Threading;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.ProjectMemory;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.Scenarios;

/// <inheritdoc />
public sealed class ScenarioFlowExecutionService : IScenarioFlowExecutionService
{
    private readonly IScenarioCatalog _catalog;
    private readonly IProjectMemoryPersonaLlmRunner _personaRunner;
    private readonly IScenarioFlowRouterLlmService _routerLlm;
    private readonly IScenarioFlowRuntimeOrchestrator? _runtimeOrchestrator;
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions> _projectMemoryOptions;
    private readonly IOptionsMonitor<ScenarioFlowHostOptions> _flowOptions;
    private readonly ILogger<ScenarioFlowExecutionService> _logger;

    public ScenarioFlowExecutionService(
        IScenarioCatalog catalog,
        IProjectMemoryPersonaLlmRunner personaRunner,
        IScenarioFlowRouterLlmService routerLlm,
        IOptionsMonitor<ProjectMemoryAgentOptions> projectMemoryOptions,
        IOptionsMonitor<ScenarioFlowHostOptions> flowOptions,
        ILogger<ScenarioFlowExecutionService> logger,
        IScenarioFlowRuntimeOrchestrator? runtimeOrchestrator = null)
    {
        _catalog = catalog;
        _personaRunner = personaRunner;
        _routerLlm = routerLlm;
        _projectMemoryOptions = projectMemoryOptions;
        _flowOptions = flowOptions;
        _logger = logger;
        _runtimeOrchestrator = runtimeOrchestrator;
    }

    /// <inheritdoc />
    public async Task<ScenarioFlowRunResponse> RunAsync(string scenarioId, ScenarioFlowRunRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return ScenarioFlowRunResponse.Fail("INVALID_SCENARIO", "Scenario id is required.");

        var message = request.Message?.Trim() ?? "";
        var hasAttachments = request.AttachmentIds != null && request.AttachmentIds.Count > 0;
        if (message.Length == 0 && !hasAttachments)
            return ScenarioFlowRunResponse.Fail("INVALID_MESSAGE", "message is required.");

        var def = _catalog.Get(scenarioId.Trim());
        if (def == null)
            return ScenarioFlowRunResponse.Fail("SCENARIO_NOT_FOUND", $"Scenario '{scenarioId}' not found.");

        if (def.Flow == null)
            return ScenarioFlowRunResponse.Fail("NO_FLOW", $"Scenario '{scenarioId}' has no flow to execute.");

        var val = ScenarioFlowValidator.Validate(def);
        if (val.Count > 0)
            return ScenarioFlowRunResponse.Fail("FLOW_INVALID", string.Join("; ", val));

        var root = _projectMemoryOptions.CurrentValue.ProjectRoot?.Trim();
        if (string.IsNullOrEmpty(root))
            return ScenarioFlowRunResponse.Fail("NO_PROJECT_ROOT", "Agctor:ProjectMemory:ProjectRoot is not set; LlmNode execution requires a project root.");

        // Default 600s per LlmNode for photo-loop curate/style; 0 disables (only request cancellation applies).
        var sec = request.LlmNodeTimeoutSeconds ?? _flowOptions.CurrentValue.LlmNodeTimeoutSeconds;
        var llmNodeTimeout = sec <= 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(Math.Clamp(sec, 5, 3600));

        if (_runtimeOrchestrator != null
            && RequiresRuntimeActor(def.Flow))
        {
            request.ProjectRoot = root;
            var runtimeResult = await _runtimeOrchestrator
                .RunAsync(scenarioId.Trim(), def, request, cancellationToken)
                .ConfigureAwait(false);

            if (!runtimeResult.Success)
            {
                return ScenarioFlowRunResponse.Fail("FLOW_RUN_FAILED", runtimeResult.ErrorMessage ?? "Scenario flow runtime failed.");
            }

            if (runtimeResult.Completed)
            {
                _logger.LogInformation("Scenario flow v2 run completed for {ScenarioId} at {ExecutionNodeId}", scenarioId, runtimeResult.ExecutionNodeId);
                return ScenarioFlowRunResponse.OkCompleted(runtimeResult.Output ?? string.Empty, runtimeResult.ExecutionNodeId, runtimeResult.Status.ToString());
            }

            _logger.LogInformation(
                "Scenario flow v2 suspended for {ScenarioId} at {ExecutionNodeId} status {Status}",
                scenarioId,
                runtimeResult.ExecutionNodeId,
                runtimeResult.Status);

            return ScenarioFlowRunResponse.OkSuspended(
                runtimeResult.PendingPrompt
                ?? runtimeResult.Output
                ?? ScenarioFlowInterimText.SuspendFallback(runtimeResult.Status),
                runtimeResult.ExecutionNodeId,
                runtimeResult.Status.ToString());
        }

        var interpreter = new ScenarioFlowGraphInterpreter();
        try
        {
            var output = await interpreter.ExecuteAsync(
                def.Flow,
                message,
                async (personaId, prompt, ct, _) =>
                {
                    var r = await _personaRunner.RunAsync(root, request.SessionId, personaId, prompt, ct, scenarioId: scenarioId.Trim())
                        .ConfigureAwait(false);
                    if (!r.Ok)
                        throw new ScenarioFlowExecutionException(r.ErrorMessage ?? "LLM node invocation failed.");
                    return r.OutputText ?? "";
                },
                llmNodeTimeout,
                root,
                _routerLlm,
                observer: null,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Scenario flow run completed for {ScenarioId}", scenarioId);
            return ScenarioFlowRunResponse.OkCompleted(output);
        }
        catch (ScenarioFlowExecutionException ex)
        {
            _logger.LogWarning(ex, "Scenario flow run failed for {ScenarioId}", scenarioId);
            return ScenarioFlowRunResponse.Fail("FLOW_RUN_FAILED", ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scenario flow run error for {ScenarioId}", scenarioId);
            return ScenarioFlowRunResponse.Fail("INTERNAL_ERROR", ex.Message);
        }
    }

    private static bool RequiresRuntimeActor(ScenarioFlowDocument flow) =>
        ScenarioFlowCapabilities.RequiresRuntimeActor(
            flow.SchemaVersion,
            flow.Nodes.Select(n => n.Type),
            (flow.Edges ?? new List<ScenarioFlowEdge>()).Select(e => e.Mode));
}
