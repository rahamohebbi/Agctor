using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Factory implementation for managing test scenarios
/// </summary>
public class ScenarioFactory : IScenarioFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IScenarioCatalog _catalog;
    private readonly Dictionary<string, Type> _scriptedHandlers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CodeGenerationChainScenario"] = typeof(CodeGenerationChainScenario),
        ["CodeGraphDemoScenario"] = typeof(CodeGraphDemoScenario)
    };

    public ScenarioFactory(IServiceProvider serviceProvider, IScenarioCatalog catalog)
    {
        _serviceProvider = serviceProvider;
        _catalog = catalog;
    }

    public IScenario? GetScenario(string scenarioName)
    {
        var def = _catalog.Get(scenarioName);
        if (def == null)
        {
            return null;
        }

        if (def.IsScripted)
        {
            if (string.IsNullOrWhiteSpace(def.Handler) || !_scriptedHandlers.TryGetValue(def.Handler, out var scenarioType))
                return null;
            var scripted = (IScenario)ActivatorUtilities.CreateInstance(_serviceProvider, scenarioType);
            if (scripted is IScenarioDefinitionAware aware)
                aware.SetDefinition(def);
            return scripted;
        }

        return ActivatorUtilities.CreateInstance<DeclarativeScenario>(_serviceProvider, def);
    }

    public IEnumerable<string> GetAvailableScenarios()
    {
        return _catalog.List().Select(x => x.Id);
    }

    public Dictionary<string, string> GetScenarioDescriptions()
    {
        var descriptions = new Dictionary<string, string>();

        foreach (var s in _catalog.List())
        {
            descriptions[s.Id] = string.IsNullOrWhiteSpace(s.Description)
                ? (string.IsNullOrWhiteSpace(s.DisplayName) ? s.Id : s.DisplayName)
                : s.Description;
        }

        return descriptions;
    }

    public IReadOnlyList<ScenarioDefinition> GetScenarioDefinitions() => _catalog.List();
} 