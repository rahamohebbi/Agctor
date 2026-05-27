using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.Tools;
using AgctorSDK.Host.Models;
using Microsoft.Extensions.Options;

using AgctorSDK.Extensions.Services;

namespace AgctorSDK.Host.Services;

/// <inheritdoc />
public sealed class ToolAgentsInsightService : IToolAgentsInsightService
{
    private readonly IAgentFactory _agentFactory;
    private readonly AgctorToolCatalog _catalog;
    private readonly IProjectAgentSpecRegistry _specRegistry;
    private readonly AgentTypeOptions _agentTypeOptions;

    public ToolAgentsInsightService(
        IAgentFactory agentFactory,
        AgctorToolCatalog catalog,
        IProjectAgentSpecRegistry specRegistry,
        IOptions<AgentTypeOptions> agentTypeOptions)
    {
        _agentFactory = agentFactory;
        _catalog = catalog;
        _specRegistry = specRegistry;
        _agentTypeOptions = agentTypeOptions?.Value ?? throw new ArgumentNullException(nameof(agentTypeOptions));
    }

    /// <inheritdoc />
    public async Task<ToolAgentsInsightResponse> GetInsightAsync(CancellationToken cancellationToken = default)
    {
        var registered = _agentFactory.GetRegisteredToolActorTypeNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalogEntries = _catalog.GetAllEntries();

        IReadOnlyList<AgentDefinitionSpec> specs = Array.Empty<AgentDefinitionSpec>();
        try
        {
            specs = await _specRegistry.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Project root missing or loader error — still return C# hints + empty YAML.
        }

        var yamlRows = new List<(AgentDefinitionSpec Spec, string Token)>();
        foreach (var spec in specs)
        {
            foreach (var raw in spec.Tools.Allow)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                yamlRows.Add((spec, raw.Trim()));
            }
        }

        var tools = new List<ToolInsightDto>();
        foreach (var entry in catalogEntries.OrderBy(e => e.ClrTypeName, StringComparer.OrdinalIgnoreCase))
        {
            var isRegistered = registered.Contains(entry.ClrTypeName);
            var assoc = new List<ToolAgentAssociationDto>();
            foreach (var (spec, token) in yamlRows)
            {
                if (HostToolYamlMatcher.TokenMatchesHostTool(token, entry))
                {
                    assoc.Add(new ToolAgentAssociationDto
                    {
                        Kind = "project-memory-yaml",
                        AgentId = spec.Id,
                        AgentLabel = string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name,
                        Source = "tools.allow",
                        Detail = token
                    });
                }
            }

            AppendCSharpHints(entry.ClrTypeName, assoc);
            AppendProjectMemoryPersonaHints(entry.ClrTypeName, specs, assoc);

            tools.Add(new ToolInsightDto
            {
                ClrTypeName = entry.ClrTypeName,
                HttpPrimaryId = entry.ExposeOnHttpApi ? entry.PrimaryId : null,
                DisplayName = string.IsNullOrWhiteSpace(entry.Discovery.Name) ? entry.ClrTypeName : entry.Discovery.Name,
                Description = entry.Discovery.Description ?? "",
                IsRegistered = isRegistered,
                Associations = DedupeAssociations(assoc)
            });
        }

        var unmapped = BuildUnmappedYamlAllows(yamlRows, catalogEntries);

        return new ToolAgentsInsightResponse
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Tools = tools,
            UnmappedYamlAllowTokens = unmapped
        };
    }

    /// <inheritdoc />
    public async Task<AgentToolsInsightResponse> GetAgentsToolInsightAsync(CancellationToken cancellationToken = default)
    {
        var toolView = await GetInsightAsync(cancellationToken).ConfigureAwait(false);
        var rows = new Dictionary<string, AgentAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in toolView.Tools)
        {
            foreach (var assoc in tool.Associations)
            {
                var key = AgentRowKey(assoc.Kind, assoc.AgentId);
                if (!rows.TryGetValue(key, out var acc))
                {
                    acc = new AgentAccumulator(assoc.AgentId, assoc.AgentLabel, assoc.Kind);
                    rows[key] = acc;
                }

                acc.AddToolIfMissing(new AgentLinkedToolDto
                {
                    ClrTypeName = tool.ClrTypeName,
                    DisplayName = tool.DisplayName,
                    HttpPrimaryId = tool.HttpPrimaryId,
                    Description = tool.Description ?? "",
                    Source = assoc.Source,
                    Detail = assoc.Detail
                });
            }
        }

        foreach (var u in toolView.UnmappedYamlAllowTokens)
        {
            var key = AgentRowKey("project-memory-yaml", u.AgentId);
            if (!rows.TryGetValue(key, out var acc))
            {
                acc = new AgentAccumulator(u.AgentId, u.AgentLabel, "project-memory-yaml");
                rows[key] = acc;
            }

            acc.Unmapped.Add(u.Token);
        }

        var list = rows.Values
            .Select(a => new AgentToolsInsightRowDto
            {
                AgentId = a.AgentId,
                AgentLabel = a.AgentLabel,
                Kind = a.Kind,
                Tools = a.Tools.Values.OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
                UnmappedYamlAllowTokens = a.Unmapped.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .OrderBy(r => r.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AgentLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AgentToolsInsightResponse
        {
            GeneratedAt = toolView.GeneratedAt,
            Agents = list
        };
    }

    private static string AgentRowKey(string kind, string agentId) => kind + "\0" + agentId;

    private sealed class AgentAccumulator
    {
        public AgentAccumulator(string agentId, string agentLabel, string kind)
        {
            AgentId = agentId;
            AgentLabel = agentLabel;
            Kind = kind;
        }

        public string AgentId { get; }
        public string AgentLabel { get; }
        public string Kind { get; }
        public Dictionary<string, AgentLinkedToolDto> Tools { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Unmapped { get; } = new();

        public void AddToolIfMissing(AgentLinkedToolDto dto)
        {
            if (!Tools.ContainsKey(dto.ClrTypeName))
                Tools[dto.ClrTypeName] = dto;
        }
    }

    private List<UnmappedYamlToolTokenDto> BuildUnmappedYamlAllows(
        List<(AgentDefinitionSpec Spec, string Token)> yamlRows,
        IReadOnlyList<AgctorToolCatalog.ToolCatalogEntry> catalogEntries)
    {
        var outList = new List<UnmappedYamlToolTokenDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (spec, token) in yamlRows)
        {
            if (HostToolYamlMatcher.IsKnownSemanticToken(token))
                continue;

            if (catalogEntries.Any(e => HostToolYamlMatcher.TokenMatchesHostTool(token, e)))
                continue;

            var key = spec.Id + "\0" + token;
            if (!seen.Add(key))
                continue;

            outList.Add(new UnmappedYamlToolTokenDto
            {
                AgentId = spec.Id,
                AgentLabel = string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name,
                Token = token
            });
        }

        return outList.OrderBy(x => x.AgentId).ThenBy(x => x.Token).ToList();
    }

    private static List<ToolAgentAssociationDto> DedupeAssociations(List<ToolAgentAssociationDto> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ToolAgentAssociationDto>();
        foreach (var r in rows)
        {
            var key = r.Kind + "\0" + r.AgentId + "\0" + (r.Source ?? "") + "\0" + (r.Detail ?? "");
            if (seen.Add(key))
                list.Add(r);
        }

        return list;
    }

    /// <summary>YAML personas that route to host tools via scenario-flow <c>toolIds</c> / playground (not always listed in <c>tools.allow</c>).</summary>
    private static void AppendProjectMemoryPersonaHints(
        string clrToolName,
        IReadOnlyList<AgentDefinitionSpec> specs,
        List<ToolAgentAssociationDto> assoc)
    {
        foreach (var (personaId, clrTool) in ProjectMemoryPersonaToolRouting.KnownRoutes)
        {
            if (!string.Equals(clrToolName, clrTool, StringComparison.OrdinalIgnoreCase))
                continue;

            var spec = specs.FirstOrDefault(s =>
                string.Equals(s.Id, personaId, StringComparison.OrdinalIgnoreCase));
            if (spec == null)
                continue;

            assoc.Add(new ToolAgentAssociationDto
            {
                Kind = "project-memory-yaml",
                AgentId = spec.Id,
                AgentLabel = string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name,
                Source = "scenario-flow-toolIds",
                Detail = "LlmNode.config.toolIds / playground routing (see AgctorToolCatalog HTTP id)"
            });
        }
    }

    private void AppendCSharpHints(string clrToolName, List<ToolAgentAssociationDto> assoc)
    {
        foreach (var (typeName, clrType) in _agentTypeOptions.AgentTypes)
        {
            if (typeof(IToolActor).IsAssignableFrom(clrType))
                continue;

            if (!CSharpAgentToolAffinities.KnownToolKeysByAgentType.TryGetValue(typeName, out var tools) || tools.Length == 0)
                continue;

            if (!tools.Any(t => string.Equals(t, clrToolName, StringComparison.OrdinalIgnoreCase)))
                continue;

            assoc.Add(new ToolAgentAssociationDto
            {
                Kind = "csharp-agent-type",
                AgentId = typeName,
                AgentLabel = typeName,
                Source = "csharp-known-pattern",
                Detail = string.Join(", ", tools.Where(t => string.Equals(t, clrToolName, StringComparison.OrdinalIgnoreCase)))
            });
        }
    }
}
