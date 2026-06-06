namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>Host tuning for PRD-024 scenario flow execution (playground + actor runtime).</summary>
public sealed class ScenarioFlowHostOptions
{
    /// <summary>Per <c>LlmNode</c> wall clock (seconds). Photo-loop post-extract runs curate + style and needs headroom.</summary>
    public int LlmNodeTimeoutSeconds { get; set; } = 600;
}
