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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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
    private List<string> _suppressedDefaultIds = new();

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

    /// <inheritdoc />
    public IReadOnlyList<string> GetSuppressedDefaultScenarioIds() => _suppressedDefaultIds;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var defaults = await ReadDocumentAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            var user = await ReadDocumentAsync(_userPath, cancellationToken).ConfigureAwait(false);
            var suppressed = NormalizeSuppressed(user?.SuppressedDefaultScenarioIds);
            _suppressedDefaultIds = suppressed;
            var merged = Merge(defaults?.Scenarios ?? new List<ScenarioDefinition>(), user?.Scenarios ?? new List<ScenarioDefinition>(), suppressed);
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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var defaults = await ReadDocumentAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            var scenarios = userDocument.Scenarios ?? new List<ScenarioDefinition>();
            var existingUser = await ReadDocumentAsync(_userPath, cancellationToken).ConfigureAwait(false);
            var suppressed = NormalizeSuppressed(
                userDocument.SuppressedDefaultScenarioIds ?? existingUser?.SuppressedDefaultScenarioIds);
            var merged = Merge(defaults?.Scenarios ?? new List<ScenarioDefinition>(), scenarios, suppressed);
            var errors = Validate(merged);
            if (errors.Count > 0)
                return (false, errors);

            userDocument.Version = 1;
            userDocument.Scenarios = scenarios;
            userDocument.SuppressedDefaultScenarioIds = suppressed;
            await PersistUserDocumentAsync(userDocument, cancellationToken).ConfigureAwait(false);
            _merged = merged;
            _suppressedDefaultIds = suppressed;
            return (true, Array.Empty<string>());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<(bool Ok, IReadOnlyList<string> Errors)> SaveScenarioFlowAsync(
        string scenarioId,
        ScenarioFlowDocument? flow,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return (false, new[] { "Scenario id is required." });

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var id = scenarioId.Trim();
            var current = Get(id);
            if (current == null)
                return (false, new[] { "SCENARIO_NOT_FOUND" });

            var updated = CloneDef(current);
            updated.Flow = flow == null ? null : ScenarioFlowDocument.Clone(flow);
            Normalize(updated);

            var userDoc = await ReadDocumentAsync(_userPath, cancellationToken).ConfigureAwait(false);
            var userList = (userDoc?.Scenarios ?? new List<ScenarioDefinition>()).Select(CloneDef).ToList();
            var idx = userList.FindIndex(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                userList[idx] = updated;
            else
                userList.Add(updated);

            var suppressed = NormalizeSuppressed(userDoc?.SuppressedDefaultScenarioIds);
            var defaults = await ReadDocumentAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            var merged = Merge(defaults?.Scenarios ?? new List<ScenarioDefinition>(), userList, suppressed);
            var errors = Validate(merged);
            if (errors.Count > 0)
                return (false, errors);

            var toWrite = new ScenarioCatalogDocument
            {
                Version = 1,
                Scenarios = userList,
                SuppressedDefaultScenarioIds = suppressed
            };
            await PersistUserDocumentAsync(toWrite, cancellationToken).ConfigureAwait(false);
            _merged = merged;
            _suppressedDefaultIds = suppressed;
            return (true, Array.Empty<string>());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<(bool Ok, IReadOnlyList<string> Errors)> CreateScenarioAsync(
        ScenarioDefinition scenario,
        CancellationToken cancellationToken = default)
    {
        if (scenario == null)
            return (false, new[] { "Scenario body is required." });

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = CloneDef(scenario);
            Normalize(normalized);
            if (string.IsNullOrWhiteSpace(normalized.Id))
                return (false, new[] { "Scenario id is required." });
            if (!string.Equals(normalized.Kind, ScenarioKinds.Declarative, StringComparison.OrdinalIgnoreCase))
                return (false, new[] { "Only declarative scenarios can be created from the dashboard (scripted scenarios need a C# handler)." });
            if (!string.IsNullOrWhiteSpace(normalized.Handler))
                return (false, new[] { "Declarative scenarios must not set handler." });

            var userDoc = await ReadDocumentAsync(_userPath, cancellationToken).ConfigureAwait(false);
            var userList = (userDoc?.Scenarios ?? new List<ScenarioDefinition>()).Select(CloneDef).ToList();
            var suppressed = NormalizeSuppressed(userDoc?.SuppressedDefaultScenarioIds);
            var defaults = await ReadDocumentAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            if (Merge(defaults?.Scenarios ?? new List<ScenarioDefinition>(), userList, suppressed)
                .Any(s => string.Equals(s.Id, normalized.Id, StringComparison.OrdinalIgnoreCase)))
                return (false, new[] { $"Scenario id '{normalized.Id}' already exists." });

            userList.Add(normalized);
            var merged = Merge(defaults?.Scenarios ?? new List<ScenarioDefinition>(), userList, suppressed);
            var errors = Validate(merged);
            if (errors.Count > 0)
                return (false, errors);

            var toWrite = new ScenarioCatalogDocument { Version = 1, Scenarios = userList, SuppressedDefaultScenarioIds = suppressed };
            await PersistUserDocumentAsync(toWrite, cancellationToken).ConfigureAwait(false);
            _merged = merged;
            _suppressedDefaultIds = suppressed;
            return (true, Array.Empty<string>());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<(bool Ok, IReadOnlyList<string> Errors)> DeleteScenarioAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return (false, new[] { "Scenario id is required." });

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var id = scenarioId.Trim();
            var userDoc = await ReadDocumentAsync(_userPath, cancellationToken).ConfigureAwait(false);
            var userList = (userDoc?.Scenarios ?? new List<ScenarioDefinition>()).Select(CloneDef).ToList();
            var suppressed = NormalizeSuppressed(userDoc?.SuppressedDefaultScenarioIds);
            var defaults = await ReadDocumentAsync(_defaultPath, cancellationToken).ConfigureAwait(false);
            var defaultIds = new HashSet<string>(
                (defaults?.Scenarios ?? new List<ScenarioDefinition>()).Where(s => !string.IsNullOrWhiteSpace(s.Id)).Select(s => s.Id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var inDefaults = defaultIds.Contains(id);
            var hadUserRow = userList.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!hadUserRow && !inDefaults)
                return (false, new[] { "SCENARIO_NOT_FOUND" });

            // Hide a shipped default only when there is no user row left (first delete removes overlay; second delete hides default).
            if (!hadUserRow && inDefaults && !suppressed.Any(s => string.Equals(s, id, StringComparison.OrdinalIgnoreCase)))
                suppressed.Add(id);

            suppressed = NormalizeSuppressed(suppressed);
            var merged = Merge(defaults?.Scenarios ?? new List<ScenarioDefinition>(), userList, suppressed);
            var errors = Validate(merged);
            if (errors.Count > 0)
                return (false, errors);

            var toWrite = new ScenarioCatalogDocument { Version = 1, Scenarios = userList, SuppressedDefaultScenarioIds = suppressed };
            await PersistUserDocumentAsync(toWrite, cancellationToken).ConfigureAwait(false);
            _merged = merged;
            _suppressedDefaultIds = suppressed;
            return (true, Array.Empty<string>());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Write user JSON via temp file + move (same-directory replace).</summary>
    private async Task PersistUserDocumentAsync(ScenarioCatalogDocument document, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_userPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        document.Version = 1;
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var tmp = _userPath + ".tmp";
        await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
        File.Move(tmp, _userPath, overwrite: true);
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
        },
        Flow = ScenarioFlowDocument.Clone(src.Flow)
    };

    private static List<ScenarioDefinition> Merge(
        List<ScenarioDefinition> defaults,
        List<ScenarioDefinition> user,
        IReadOnlyList<string>? suppressedDefaultIds)
    {
        var suppressed = new HashSet<string>(
            (suppressedDefaultIds ?? Array.Empty<string>()).Select(s => (s ?? string.Empty).Trim()).Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, ScenarioDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defaults)
        {
            if (string.IsNullOrWhiteSpace(d.Id)) continue;
            var key = d.Id.Trim();
            if (suppressed.Contains(key))
                continue;
            map[key] = CloneDef(Normalize(d));
        }

        foreach (var u in user)
        {
            if (string.IsNullOrWhiteSpace(u.Id)) continue;
            map[u.Id.Trim()] = CloneDef(Normalize(u));
        }

        return map.Values.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> NormalizeSuppressed(IEnumerable<string>? src) =>
        (src ?? Enumerable.Empty<string>())
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
        NormalizeFlow(d);
        return d;
    }

    private static void NormalizeFlow(ScenarioDefinition d)
    {
        var f = d.Flow;
        if (f == null) return;
        f.SchemaVersion = string.IsNullOrWhiteSpace(f.SchemaVersion) ? "1.0" : f.SchemaVersion.Trim();
        f.GraphId = (f.GraphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(f.GraphId) && !string.IsNullOrWhiteSpace(d.Id))
            f.GraphId = d.Id.Trim() + "-flow";
        f.Name = string.IsNullOrWhiteSpace(f.Name) ? null : f.Name.Trim();
        f.Status = string.IsNullOrWhiteSpace(f.Status) ? null : f.Status.Trim().ToLowerInvariant();
        f.OutputPolicy = string.IsNullOrWhiteSpace(f.OutputPolicy) ? "merge_sections" : f.OutputPolicy.Trim().ToLowerInvariant();
        f.Nodes ??= new List<ScenarioFlowNode>();
        f.Edges ??= new List<ScenarioFlowEdge>();
        foreach (var n in f.Nodes)
        {
            n.Id = (n.Id ?? string.Empty).Trim();
            n.Type = (n.Type ?? string.Empty).Trim();
            n.Label = (n.Label ?? string.Empty).Trim();
        }

        foreach (var e in f.Edges)
        {
            e.Id = (e.Id ?? string.Empty).Trim();
            e.FromNodeId = (e.FromNodeId ?? string.Empty).Trim();
            e.ToNodeId = (e.ToNodeId ?? string.Empty).Trim();
            e.Mode = string.IsNullOrWhiteSpace(e.Mode) ? "sequential" : e.Mode.Trim().ToLowerInvariant();
            e.Condition = string.IsNullOrWhiteSpace(e.Condition) ? null : e.Condition.Trim();
        }
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

            errors.AddRange(ScenarioFlowValidator.Validate(d));
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

