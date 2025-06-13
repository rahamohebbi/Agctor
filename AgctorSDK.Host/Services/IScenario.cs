using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Interface for defining test scenarios that create predefined agent configurations
/// </summary>
public interface IScenario
{
    /// <summary>
    /// The unique name of this scenario
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Description of what this scenario creates
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Set up the scenario by creating and configuring agents
    /// </summary>
    /// <param name="parameters">Optional parameters for scenario customization</param>
    /// <returns>Setup response with created agent information</returns>
    Task<ScenarioSetupResponse> SetupAsync(Dictionary<string, object>? parameters = null);
} 