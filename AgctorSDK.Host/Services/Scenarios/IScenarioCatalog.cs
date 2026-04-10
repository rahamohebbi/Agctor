namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Loads and validates scenario definitions from JSON files.
/// </summary>
public interface IScenarioCatalog
{
    IReadOnlyList<ScenarioDefinition> List();
    ScenarioDefinition? Get(string id);
    Task ReloadAsync(CancellationToken cancellationToken = default);
    Task<(bool Ok, IReadOnlyList<string> Errors)> SaveAsync(ScenarioCatalogDocument userDocument, CancellationToken cancellationToken = default);
}

