using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Factory implementation for managing test scenarios
/// </summary>
public class ScenarioFactory : IScenarioFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _scenarios;

    public ScenarioFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _scenarios = new Dictionary<string, Type>
        {
            { "code-generation-chain", typeof(CodeGenerationChainScenario) }
            // Add new scenarios here:
            // { "math-generation-chain", typeof(MathGenerationChainScenario) }
        };
    }

    public IScenario? GetScenario(string scenarioName)
    {
        if (!_scenarios.TryGetValue(scenarioName, out var scenarioType))
        {
            return null;
        }

        return (IScenario)ActivatorUtilities.CreateInstance(_serviceProvider, scenarioType);
    }

    public IEnumerable<string> GetAvailableScenarios()
    {
        return _scenarios.Keys;
    }

    public Dictionary<string, string> GetScenarioDescriptions()
    {
        var descriptions = new Dictionary<string, string>();
        
        foreach (var (name, type) in _scenarios)
        {
            var scenario = (IScenario)ActivatorUtilities.CreateInstance(_serviceProvider, type);
            descriptions[name] = scenario.Description;
        }
        
        return descriptions;
    }
} 