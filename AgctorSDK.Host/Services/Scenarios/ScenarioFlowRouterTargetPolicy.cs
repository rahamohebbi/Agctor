namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// How an LLM <c>Router</c> chooses outgoing <c>LlmNode</c> branches after Ollama returns targets.
/// </summary>
public enum ScenarioFlowRouterTargetPolicy
{
    /// <summary>Every target above <c>minConfidence</c> may run (optional <c>maxTargets</c> cap).</summary>
    AllMatching = 0,

    /// <summary>At most one branch — highest-confidence persona that matches routing hints.</summary>
    SingleBest = 1
}
