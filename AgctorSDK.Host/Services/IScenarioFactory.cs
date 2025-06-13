namespace AgctorSDK.Host.Services;

/// <summary>
/// Factory for creating and managing test scenarios
/// </summary>
public interface IScenarioFactory
{
    /// <summary>
    /// Get a scenario by name
    /// </summary>
    /// <param name="scenarioName">Name of the scenario</param>
    /// <returns>The scenario instance, or null if not found</returns>
    IScenario? GetScenario(string scenarioName);
    
    /// <summary>
    /// Get all available scenario names
    /// </summary>
    /// <returns>List of available scenario names</returns>
    IEnumerable<string> GetAvailableScenarios();
    
    /// <summary>
    /// Get scenario information (name and description) for all scenarios
    /// </summary>
    /// <returns>Dictionary of scenario name to description</returns>
    Dictionary<string, string> GetScenarioDescriptions();
} 