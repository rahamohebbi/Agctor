namespace AgctorSDK.Host.Services;

/// <summary>
/// Stores the scenario that was last successfully applied in this session (for dashboard display).
/// </summary>
public interface ICurrentScenarioStore
{
    /// <summary>
    /// Gets the name of the currently active scenario, or null if none has been applied.
    /// </summary>
    string? GetCurrentScenarioName();

    /// <summary>
    /// Gets the description of the currently active scenario, or null if none.
    /// </summary>
    string? GetCurrentScenarioDescription();

    /// <summary>
    /// Sets the current scenario after a successful setup (called by TestController).
    /// </summary>
    void SetCurrentScenario(string scenarioName, string? description = null);

    /// <summary>
    /// Clears the current scenario (e.g. when the app is reset).
    /// </summary>
    void Clear();
}
