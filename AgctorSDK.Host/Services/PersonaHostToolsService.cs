using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Extensions.Services;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>Builds persona tool panels from YAML specs + <see cref="PersonaHostToolCatalog"/>.</summary>
public sealed class PersonaHostToolsService : IPersonaHostToolsService
{
    private readonly IProjectAgentSpecRegistry _specRegistry;
    private readonly AgctorToolCatalog _catalog;

    public PersonaHostToolsService(IProjectAgentSpecRegistry specRegistry, AgctorToolCatalog catalog)
    {
        _specRegistry = specRegistry ?? throw new ArgumentNullException(nameof(specRegistry));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<PersonaHostToolsResponseDto> GetForPersonaAsync(
        string personaId,
        CancellationToken cancellationToken = default)
    {
        var pid = string.IsNullOrWhiteSpace(personaId) ? "" : personaId.Trim();
        var response = new PersonaHostToolsResponseDto { PersonaId = pid };

        if (string.IsNullOrEmpty(pid))
            return response;

        var spec = await _specRegistry.GetByIdAsync(pid, cancellationToken).ConfigureAwait(false);
        response.AgentFound = spec != null;
        if (spec != null)
            response.AgentLabel = string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name;

        var yamlAllow = spec?.Tools.Allow.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList()
            ?? new List<string>();
        var yamlDeny = spec?.Tools.Deny.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList()
            ?? new List<string>();
        response.YamlAllow = yamlAllow;
        response.YamlDeny = yamlDeny;

        var allowSet = new HashSet<string>(yamlAllow, StringComparer.OrdinalIgnoreCase);
        var catalogByHttpId = _catalog
            .GetAllEntries()
            .Where(e => e.ExposeOnHttpApi)
            .ToDictionary(e => e.PrimaryId, StringComparer.OrdinalIgnoreCase);

        var hostOptions = new List<PersonaHostToolOptionDto>();
        foreach (var def in PersonaHostToolCatalog.ForPersona(pid))
        {
            catalogByHttpId.TryGetValue(def.Id, out var entry);
            var matched = entry != null ? HostToolYamlMatcher.FindMatchingToken(yamlAllow, entry) : null;
            var isAllowed = matched != null || allowSet.Contains(def.Id);
            hostOptions.Add(new PersonaHostToolOptionDto
            {
                Id = def.Id,
                Group = def.Group,
                Name = entry?.Discovery.Name ?? def.Id,
                Description = entry?.Discovery.Description ?? "",
                IsAllowed = isAllowed,
                MatchedYamlToken = matched
            });
        }

        response.HostTools = hostOptions;

        var semantic = new List<PersonaSemanticToolOptionDto>();
        foreach (var (id, label) in PersonaHostToolCatalog.SemanticTokens)
        {
            semantic.Add(new PersonaSemanticToolOptionDto
            {
                Id = id,
                Label = label,
                IsAllowed = allowSet.Contains(id)
            });
        }

        response.SemanticTools = semantic;

        var custom = new List<string>();
        foreach (var token in yamlAllow)
        {
            if (HostToolYamlMatcher.IsKnownSemanticToken(token))
                continue;

            var matchesHost = _catalog.GetAllEntries().Any(e => HostToolYamlMatcher.TokenMatchesHostTool(token, e));
            if (!matchesHost)
                custom.Add(token);
        }

        response.CustomAllowTokens = custom;
        return response;
    }
}
