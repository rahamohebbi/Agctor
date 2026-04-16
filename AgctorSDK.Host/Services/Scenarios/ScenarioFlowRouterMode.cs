namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>PRD-014 Phase 10: how a <c>Router</c> node chooses outgoing <c>PersonaCall</c> branches.</summary>
public enum ScenarioFlowRouterMode
{
    /// <summary>Substring match on edge <c>condition</c> + single default edge (legacy).</summary>
    Deterministic,

    /// <summary>LLM picks one or more <c>config.personaId</c> values from sequential Router→PersonaCall edges.</summary>
    Llm
}
