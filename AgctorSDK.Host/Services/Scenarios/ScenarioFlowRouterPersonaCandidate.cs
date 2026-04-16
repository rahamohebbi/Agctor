namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>One Router→PersonaCall candidate (graph-derived, edge order preserved elsewhere).</summary>
public sealed record ScenarioFlowRouterPersonaCandidate(string NodeId, string PersonaId, string? Label);
