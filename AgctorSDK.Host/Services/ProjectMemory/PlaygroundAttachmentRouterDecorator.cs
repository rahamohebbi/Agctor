using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>Wraps the LLM router with 023e pre-router when the turn includes photos.</summary>
public sealed class PlaygroundAttachmentRouterDecorator : IScenarioFlowRouterLlmService
{
    private readonly IScenarioFlowRouterLlmService _inner;
    private readonly PlaygroundFlowRoutingContext _routingContext;

    public PlaygroundAttachmentRouterDecorator(
        IScenarioFlowRouterLlmService inner,
        PlaygroundFlowRoutingContext routingContext)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _routingContext = routingContext ?? throw new ArgumentNullException(nameof(routingContext));
    }

    public Task<ScenarioFlowRouterLlmResult> RouteAsync(
        string projectRoot,
        string userMessage,
        IReadOnlyList<ScenarioFlowRouterPersonaCandidate> candidates,
        ScenarioFlowRouterConfig config,
        CancellationToken cancellationToken = default,
        string? routingContext = null)
    {
        if (PlaygroundFlowAttachmentRouting.TryPickPersona(
                _routingContext,
                userMessage,
                candidates,
                out var personaId)
            && !string.IsNullOrWhiteSpace(personaId))
        {
            return Task.FromResult(ScenarioFlowRouterLlmResult.Success(new[] { personaId }));
        }

        return _inner.RouteAsync(
            projectRoot,
            userMessage,
            candidates,
            config,
            cancellationToken,
            routingContext);
    }
}
