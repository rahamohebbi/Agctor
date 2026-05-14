namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>One Router→LlmNode candidate (graph-derived; <see cref="EdgeId"/> ties rules to a specific edge).</summary>
public sealed record ScenarioFlowRouterPersonaCandidate(
    string NodeId,
    string PersonaId,
    string? Label,
    string EdgeId,
    string? LlmRoutingHint);
