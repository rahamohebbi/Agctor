namespace AgctorSDK.Host.Services;

/// <summary>
/// In-memory store for the scenario last applied in this session. Thread-safe.
/// </summary>
public sealed class CurrentScenarioStore : ICurrentScenarioStore
{
    private readonly object _lock = new();
    private string? _name;
    private string? _description;

    /// <inheritdoc />
    public string? GetCurrentScenarioName()
    {
        lock (_lock)
            return _name;
    }

    /// <inheritdoc />
    public string? GetCurrentScenarioDescription()
    {
        lock (_lock)
            return _description;
    }

    /// <inheritdoc />
    public void SetCurrentScenario(string scenarioName, string? description = null)
    {
        lock (_lock)
        {
            _name = scenarioName ?? throw new ArgumentNullException(nameof(scenarioName));
            _description = description;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _name = null;
            _description = null;
        }
    }
}
