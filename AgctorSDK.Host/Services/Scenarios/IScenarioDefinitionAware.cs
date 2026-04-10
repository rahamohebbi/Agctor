namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Optional hook for scripted scenarios to receive catalog metadata.
/// </summary>
public interface IScenarioDefinitionAware
{
    void SetDefinition(ScenarioDefinition definition);
}

