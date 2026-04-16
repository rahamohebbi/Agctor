namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>PRD-014 Phase 10: LLM chooses persona targets for a <c>Router</c> with <c>routerMode: llm</c>.</summary>
public interface IScenarioFlowRouterLlmService
{
    /// <param name="projectRoot">Loads YAML agent specs for candidate blurbs.</param>
    /// <param name="userMessage">Original chat line (kept for logging/back-compat).</param>
    /// <param name="routingContext">Text the router reasons over; when null/empty, <paramref name="userMessage"/> is used.</param>
    Task<ScenarioFlowRouterLlmResult> RouteAsync(
        string projectRoot,
        string userMessage,
        IReadOnlyList<ScenarioFlowRouterPersonaCandidate> candidates,
        ScenarioFlowRouterConfig config,
        CancellationToken cancellationToken = default,
        string? routingContext = null);
}
