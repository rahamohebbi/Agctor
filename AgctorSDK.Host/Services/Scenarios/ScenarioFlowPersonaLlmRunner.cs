using System.Text.Json;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Coref;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Host.Services.ProjectMemory;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Flow-segment persona runner with playground-equivalent memory and visual context appendices.
/// </summary>
public sealed class ScenarioFlowPersonaLlmRunner : IScenarioFlowPersonaLlmRunner
{
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;
    private readonly IAgentFactory _agentFactory;
    private readonly IProjectMemoryLlmClient _llm;
    private readonly IConversationFocusStore _focusStore;
    private readonly ILogger<ScenarioFlowPersonaLlmRunner> _logger;

    public ScenarioFlowPersonaLlmRunner(
        IProjectLoader loader,
        IEntityRegistry entities,
        IAgentFactory agentFactory,
        IProjectMemoryLlmClient llm,
        IConversationFocusStore focusStore,
        ILogger<ScenarioFlowPersonaLlmRunner> logger)
    {
        _loader = loader;
        _entities = entities;
        _agentFactory = agentFactory;
        _llm = llm;
        _focusStore = focusStore;
        _logger = logger;
    }

    public async Task<ProjectMemoryPersonaRunResult> RunFlowNodeAsync(
        ScenarioFlowPersonaRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectRoot))
            return new ProjectMemoryPersonaRunResult(false, "Project root is required.", null);
        if (string.IsNullOrWhiteSpace(request.AgentId))
            return new ProjectMemoryPersonaRunResult(false, "agentId is required.", null);
        if (string.IsNullOrWhiteSpace(request.InputText))
            return new ProjectMemoryPersonaRunResult(false, "inputText is required.", null);

        LoadedProjectContext ctx;
        try
        {
            ctx = await _loader.LoadAsync(request.ProjectRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scenario flow persona: failed to load project at {Root}", request.ProjectRoot);
            return new ProjectMemoryPersonaRunResult(false, ex.Message, null);
        }

        var agentId = request.AgentId.Trim();
        var spec = ctx.AgentSpecs.FirstOrDefault(a =>
            string.Equals(a.Id, agentId, StringComparison.OrdinalIgnoreCase));
        if (spec == null)
            return new ProjectMemoryPersonaRunResult(false, $"Agent spec '{agentId}' not found.", null);

        var rootFull = Path.GetFullPath(request.ProjectRoot.Trim());
        var scenarioId = request.ScenarioId.Trim();
        var focusEntityKey = await ResolveFocusEntityKeyAsync(rootFull, scenarioId, cancellationToken)
            .ConfigureAwait(false);

        var appendixParts = new List<string>();
        if (PlaygroundPersonQueryContextBuilder.ShouldLoadPersonMemoryContext(spec, request.FlowNodeConfig))
        {
            try
            {
                var strat = PlaygroundPersonQueryContextBuilder.ParseStrategy(request.FlowNodeConfig);
                var memoryAppendix = await PlaygroundPersonQueryContextBuilder
                    .BuildFlowAppendixAsync(
                        _loader,
                        _entities,
                        _agentFactory,
                        spec,
                        agentId,
                        rootFull,
                        scenarioId,
                        strat,
                        request.InputText,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(memoryAppendix))
                    appendixParts.Add(memoryAppendix);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scenario flow persona: person-memory-context failed for {AgentId}", agentId);
            }
        }

        if (PlaygroundPersonQueryContextBuilder.ShouldLoadPersonVisualContext(spec, request.FlowNodeConfig))
        {
            try
            {
                var sessionAssets = request.SessionAssetIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .ToList();
                var maxPhotos = sessionAssets is { Count: > 0 }
                    ? Math.Clamp(sessionAssets.Count, 1, 12)
                    : ChatProjectSettings.ResolveVisualMaxPhotos(null, defaultValue: 3, maxCap: 12);
                var entityFilter = request.RelaxVisualEntityFilter ? null : focusEntityKey;
                var visualContext = await PlaygroundPersonQueryContextBuilder
                    .BuildVisualContextAsync(
                        _agentFactory,
                        spec,
                        agentId,
                        rootFull,
                        scenarioId,
                        request.InputText,
                        entityFilter,
                        maxPhotos,
                        cancellationToken,
                        sessionAssets)
                    .ConfigureAwait(false);

                // Focus slug may not match photo subjects — load full scenario catalog when empty.
                if (visualContext.Assets.Count == 0 && sessionAssets is not { Count: > 0 })
                {
                    visualContext = await PlaygroundPersonQueryContextBuilder
                        .BuildVisualContextAsync(
                            _agentFactory,
                            spec,
                            agentId,
                            rootFull,
                            scenarioId,
                            request.InputText,
                            focusEntityKey: null,
                            maxPhotos,
                            cancellationToken,
                            sessionAssets)
                        .ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(visualContext.Appendix))
                    appendixParts.Add(visualContext.Appendix);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scenario flow persona: person-visual-context failed for {AgentId}", agentId);
            }
        }

        var flowAppendix = appendixParts.Count > 0 ? string.Join("\n\n", appendixParts) : null;
        var prompt = ProjectMemoryPersonaLlmRunner.BuildPlaygroundPrompt(
            spec,
            priorTurns: null,
            newUserText: request.InputText,
            scenarioId: scenarioId,
            playgroundFlowAppendix: flowAppendix,
            activeSubjectEntityKey: focusEntityKey);

        try
        {
            var output = await _llm.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            return new ProjectMemoryPersonaRunResult(true, null, output);
        }
        catch (OperationCanceledException)
        {
            return new ProjectMemoryPersonaRunResult(
                false,
                "LLM request timed out or was cancelled. Try again or increase Agctor:ScenarioFlow:LlmNodeTimeoutSeconds.",
                null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scenario flow persona: LLM failed for {AgentId}", agentId);
            return new ProjectMemoryPersonaRunResult(false, ex.Message, null);
        }
    }

    private async Task<string?> ResolveFocusEntityKeyAsync(
        string projectRoot,
        string scenarioId,
        CancellationToken cancellationToken)
    {
        try
        {
            var focus = await _focusStore
                .LoadAsync(projectRoot, scenarioId, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(focus?.EntityKey) ? null : focus.EntityKey.Trim();
        }
        catch
        {
            return null;
        }
    }
}
