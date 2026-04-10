using System.Text.Json;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.ProjectMemory;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// File-backed scenario catalog with default + user overlay merge.
/// </summary>
public sealed class JsonScenarioCatalog : IScenarioCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _defaultPath;
    private readonly string _userPath;
    private readonly AgentTypeOptions _agentTypeOptions;
    private readonly IProjectAgentSpecRegistry _projectAgentSpecs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonScenarioCatalog> _logger;

    private List<ScenarioDefinition> _merged = new();

    public JsonScenarioCatalog(
        IWebHostEnvironment env,
        IOptions<ScenarioCatalogOptions> options,
        IOptions<AgentTypeOptions> agentTypeOptions,
        IProjectAgentSpecRegistry projectAgentSpecs,
        ILogger<JsonScenarioCatalog> logger)
    {
        var o = options.Value;
        _defaultPath = ResolvePath(env.ContentRootPath, o.DefaultFile);
        _userPath = ResolvePath(env.ContentRootPath, o.UserFile);
        _agentTypeOptions = agentTypeOptions.Value;
        _projectAgentSpecs = projectAgentSpecs;
        _logger = logger;
        ReloadAsync().GetAwaiter().GetResult();
    }

    public IReadOnlyList<ScenarioDefinition> List() => _merged.Select(CloneDef).ToList();

    public ScenarioDefinition? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var match = _merged.FirstOrDefault(x => string.Equals(x.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        return match == null ? null : CloneDef(match);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var defaults = await ReadDocumentAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            var user = await ReadDocumentAsync(_userPath, cancellationToken).ConfigureAwait(false);
            var merged = Merge(defaults?.Scenarios ?? new List<ScenarioDefinition>(), user?.Scenarios ?? new List<ScenarioDefinition>());
            var errors = Validate(merged);
            if (errors.Count > 0)
            {
                _logger.LogWarning("Scenario catalog loaded with validation issues: {Errors}", string.Join(" | ", errors));
            }
            _merged = merged;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(bool Ok, IReadOnlyList<string> Errors)> SaveAsync(ScenarioCatalogDocument userDocument, CancellationToken cancellationToken = default)
    {
        if (userDocument == null) return (false, new[] { "Request body is required." });

        var errors = Validate(userDocument.Scenarios ?? new List<ScenarioDefinition>());
        if (errors.Count > 0) return (false, errors);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(_userPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            userDocument.Version = 1;
            await File.WriteAllTextAsync(_userPath, JsonSerializer.Serialize(userDocument, JsonOptions), cancellationToken).ConfigureAwait(false);
            var defaults = await ReadDocumentAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            var merged = Merge(defaults?.Scenarios ?? new List<ScenarioDefinition>(), userDocument.Scenarios ?? new List<ScenarioDefinition>());
            _merged = merged;
            return (true, Array.Empty<string>());
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ResolvePath(string root, string configured)
    {
        if (Path.IsPathRooted(configured)) return configured;
        return Path.GetFullPath(Path.Combine(root, configured ?? string.Empty));
    }

    private static ScenarioDefinition CloneDef(ScenarioDefinition src) => new()
    {
        Id = src.Id,
        DisplayName = src.DisplayName,
        Description = src.Description,
        Kind = src.Kind,
        Handler = src.Handler,
        AgentTypes = src.AgentTypes?.ToList() ?? new List<string>(),
        PersonaAgentIds = src.PersonaAgentIds?.ToList() ?? new List<string>(),
        PersonaBindings = new ScenarioPersonaBindings
        {
            Extractor = src.PersonaBindings?.Extractor,
            Curator = src.PersonaBindings?.Curator,
            Query = src.PersonaBindings?.Query
        }
    };

    private static List<ScenarioDefinition> Merge(List<ScenarioDefinition> defaults, List<ScenarioDefinition> user)
    {
        var map = new Dictionary<string, ScenarioDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defaults)
        {
            if (string.IsNullOrWhiteSpace(d.Id)) continue;
            map[d.Id.Trim()] = CloneDef(Normalize(d));
        }
        foreach (var u in user)
        {
            if (string.IsNullOrWhiteSpace(u.Id)) continue;
            map[u.Id.Trim()] = CloneDef(Normalize(u));
        }
        return map.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ScenarioDefinition Normalize(ScenarioDefinition d)
    {
        d.Id = (d.Id ?? string.Empty).Trim();
        d.DisplayName = (d.DisplayName ?? string.Empty).Trim();
        d.Description = (d.Description ?? string.Empty).Trim();
        d.Kind = string.IsNullOrWhiteSpace(d.Kind) ? ScenarioKinds.Declarative : d.Kind.Trim().ToLowerInvariant();
        d.Handler = string.IsNullOrWhiteSpace(d.Handler) ? null : d.Handler.Trim();
        d.AgentTypes = (d.AgentTypes ?? new List<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        d.PersonaAgentIds = (d.PersonaAgentIds ?? new List<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        d.PersonaBindings ??= new ScenarioPersonaBindings();
        d.PersonaBindings.Extractor = string.IsNullOrWhiteSpace(d.PersonaBindings.Extractor) ? null : d.PersonaBindings.Extractor.Trim();
        d.PersonaBindings.Curator = string.IsNullOrWhiteSpace(d.PersonaBindings.Curator) ? null : d.PersonaBindings.Curator.Trim();
        d.PersonaBindings.Query = string.IsNullOrWhiteSpace(d.PersonaBindings.Query) ? null : d.PersonaBindings.Query.Trim();
        return d;
    }

    private List<string> Validate(List<ScenarioDefinition> defs)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validAgentTypeNames = new HashSet<string>(_agentTypeOptions.AgentTypes.Keys, StringComparer.OrdinalIgnoreCase);
        var validPersonaIds = new HashSet<string>(
            _projectAgentSpecs.GetAllAsync().GetAwaiter().GetResult().Select(x => x.Id),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < defs.Count; i++)
        {
            var d = Normalize(defs[i]);
            if (string.IsNullOrWhiteSpace(d.Id))
            {
                errors.Add($"Scenario at index {i} is missing id.");
                continue;
            }
            if (!ids.Add(d.Id))
                errors.Add($"Duplicate scenario id '{d.Id}'.");

            if (!string.Equals(d.Kind, ScenarioKinds.Declarative, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(d.Kind, ScenarioKinds.Scripted, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Scenario '{d.Id}' has invalid kind '{d.Kind}'.");
            }
            if (d.IsScripted && string.IsNullOrWhiteSpace(d.Handler))
                errors.Add($"Scenario '{d.Id}' is scripted and requires handler.");
            if (!d.IsScripted && !string.IsNullOrWhiteSpace(d.Handler))
                errors.Add($"Scenario '{d.Id}' is declarative and must not set handler.");

            foreach (var agentType in d.AgentTypes)
            {
                if (!validAgentTypeNames.Contains(agentType))
                    errors.Add($"Scenario '{d.Id}' references unknown agent type '{agentType}'.");
            }

            foreach (var personaId in d.PersonaAgentIds)
            {
                if (!validPersonaIds.Contains(personaId))
                    errors.Add($"Scenario '{d.Id}' references unknown persona agent id '{personaId}'.");
            }

            var bindings = new[] { d.PersonaBindings.Extractor, d.PersonaBindings.Curator, d.PersonaBindings.Query }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();
            foreach (var b in bindings)
            {
                if (!d.PersonaAgentIds.Contains(b, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"Scenario '{d.Id}' binding '{b}' must be included in personaAgentIds.");
            }
        }

        return errors;
    }

    private static async Task<ScenarioCatalogDocument?> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return JsonSerializer.Deserialize<ScenarioCatalogDocument>(text, JsonOptions);
    }
}

