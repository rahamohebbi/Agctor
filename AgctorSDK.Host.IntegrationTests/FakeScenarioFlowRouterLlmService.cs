using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>Deterministic router LLM for interpreter tests (no Ollama).</summary>
internal sealed class FakeScenarioFlowRouterLlmService : IScenarioFlowRouterLlmService
{
    public ScenarioFlowRouterLlmResult Next { get; set; } =
        ScenarioFlowRouterLlmResult.Fail("FakeScenarioFlowRouterLlmService.Next not set.");

    /// <summary>Last <paramref name="routingContext"/> argument (null means interpreter passed null).</summary>
    public string? LastRoutingContext { get; private set; }

    public Task<ScenarioFlowRouterLlmResult> RouteAsync(
        string projectRoot,
        string userMessage,
        IReadOnlyList<ScenarioFlowRouterPersonaCandidate> candidates,
        ScenarioFlowRouterConfig config,
        CancellationToken cancellationToken = default,
        string? routingContext = null)
    {
        LastRoutingContext = routingContext;
        return Task.FromResult(Next);
    }
}
