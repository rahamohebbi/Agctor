using System.Threading;
using AgctorSDK.Core.ProjectMemory;
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
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions> _projectMemoryOptions;
    private readonly ILogger<ScenarioFlowExecutionService> _logger;

    public ScenarioFlowExecutionService(
        IScenarioCatalog catalog,
        IProjectMemoryPersonaLlmRunner personaRunner,
        IScenarioFlowRouterLlmService routerLlm,
        IOptionsMonitor<ProjectMemoryAgentOptions> projectMemoryOptions,
        ILogger<ScenarioFlowExecutionService> logger)
    {
        _catalog = catalog;
        _personaRunner = personaRunner;
        _routerLlm = routerLlm;
        _projectMemoryOptions = projectMemoryOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScenarioFlowRunResponse> RunAsync(string scenarioId, ScenarioFlowRunRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return ScenarioFlowRunResponse.Fail("INVALID_SCENARIO", "Scenario id is required.");

        var message = request.Message?.Trim() ?? "";
        if (message.Length == 0)
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

        // Default 180s per LlmNode; 0 disables (only request cancellation applies).
        var sec = request.LlmNodeTimeoutSeconds ?? 180;
        var llmNodeTimeout = sec <= 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(Math.Clamp(sec, 5, 3600));

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
            return ScenarioFlowRunResponse.Ok(output);
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
}
