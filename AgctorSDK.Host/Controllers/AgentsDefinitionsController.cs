using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// Unified agent definitions API (PRD-013). Lives under <c>/api/agents/definitions</c> so routes do not clash with <c>GET /api/agents/{agentId}</c>.
/// </summary>
[ApiController]
[Route("api/agents/definitions")]
[Produces("application/json")]
public sealed class AgentsDefinitionsController : ControllerBase
{
    private readonly IAgentTypeEnablementService _agentTypeEnablement;
    private readonly AgentTypeOptions _agentTypeOptions;
    private readonly IProjectAgentSpecRegistry _projectAgentSpecRegistry;
    private readonly IProjectMemoryAgentYamlPersistence _projectMemoryAgentYaml;
    private readonly IToolAgentsInsightService _toolAgentsInsight;
    private readonly ILogger<AgentsDefinitionsController> _logger;

    public AgentsDefinitionsController(
        IAgentTypeEnablementService agentTypeEnablement,
        IOptions<AgentTypeOptions> agentTypeOptions,
        IProjectAgentSpecRegistry projectAgentSpecRegistry,
        IProjectMemoryAgentYamlPersistence projectMemoryAgentYaml,
        IToolAgentsInsightService toolAgentsInsight,
        ILogger<AgentsDefinitionsController> logger)
    {
        _agentTypeEnablement = agentTypeEnablement ?? throw new ArgumentNullException(nameof(agentTypeEnablement));
        _agentTypeOptions = agentTypeOptions?.Value ?? throw new ArgumentNullException(nameof(agentTypeOptions));
        _projectAgentSpecRegistry = projectAgentSpecRegistry ?? throw new ArgumentNullException(nameof(projectAgentSpecRegistry));
        _projectMemoryAgentYaml = projectMemoryAgentYaml ?? throw new ArgumentNullException(nameof(projectMemoryAgentYaml));
        _toolAgentsInsight = toolAgentsInsight ?? throw new ArgumentNullException(nameof(toolAgentsInsight));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Unified catalog: C# types + project-memory YAML specs.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AgentDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IEnumerable<AgentDefinitionDto>>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new List<AgentDefinitionDto>();

            foreach (var (typeName, clrType) in _agentTypeOptions.AgentTypes.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var enabled = _agentTypeEnablement.IsTypeEnabled(typeName);
                result.Add(new AgentDefinitionDto
                {
                    Id = typeName,
                    DisplayName = typeName,
                    Kind = "csharp-type",
                    Source = clrType.FullName ?? clrType.Name,
                    State = enabled ? "enabled" : "disabled",
                    Metadata = new Dictionary<string, object> { ["clrType"] = clrType.Name }
                });
            }

            var specs = await _projectAgentSpecRegistry.GetAllAsync(cancellationToken).ConfigureAwait(false);
            foreach (var spec in specs.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new AgentDefinitionDto
                {
                    Id = spec.Id,
                    DisplayName = string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name,
                    Kind = "project-memory-yaml",
                    Source = string.IsNullOrWhiteSpace(spec.SourcePath) ? ".agctor/agents" : spec.SourcePath,
                    State = "valid",
                    Metadata = new Dictionary<string, object>
                    {
                        ["projectTypes"] = spec.ProjectTypes,
                        ["role"] = spec.Role
                    }
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving unified agent definitions");
            return StatusCode(500, new ErrorResponse
            {
                Code = "INTERNAL_ERROR",
                Message = "An internal error occurred while retrieving agent definitions."
            });
        }
    }

    /// <summary>
    /// Dashboard: each agent (YAML spec or C# type) with host <see cref="AgctorSDK.Core.Tools.IToolActor"/> tools it may use.
    /// Declared before <c>{id}</c> so <c>tool-usage</c> is not captured as an agent id.
    /// </summary>
    [HttpGet("tool-usage")]
    [ProducesResponseType(typeof(AgentToolsInsightResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AgentToolsInsightResponse>> GetAgentToolUsageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var dto = await _toolAgentsInsight.GetAgentsToolInsightAsync(cancellationToken).ConfigureAwait(false);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building agent tool usage");
            return StatusCode(500, new ErrorResponse
            {
                Code = "INTERNAL_ERROR",
                Message = "An internal error occurred while building agent tool usage."
            });
        }
    }

    /// <summary>C# type metadata or loaded YAML spec + preview.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AgentDefinitionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentDefinitionDetailDto>> GetDefinitionByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "id required." });

        var typeEntry = _agentTypeOptions.AgentTypes.FirstOrDefault(kv =>
            string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(typeEntry.Key))
        {
            var clr = typeEntry.Value;
            return Ok(new AgentDefinitionDetailDto
            {
                Kind = "csharp-type",
                Id = typeEntry.Key,
                Detail = new CSharpAgentDefinitionDetailDto
                {
                    Enabled = _agentTypeEnablement.IsTypeEnabled(typeEntry.Key),
                    ClrType = clr.FullName ?? clr.Name
                }
            });
        }

        var yaml = await _projectMemoryAgentYaml.GetAgentDetailAsync(id, cancellationToken).ConfigureAwait(false);
        return yaml.StatusCode switch
        {
            200 => Ok(new AgentDefinitionDetailDto
            {
                Kind = "project-memory-yaml",
                Id = id,
                Detail = yaml.Data
            }),
            404 => NotFound(yaml.Error),
            _ => StatusCode(yaml.StatusCode, yaml.Error)
        };
    }

    /// <summary>Creates a new <c>*.agent.yaml</c> (409 if id already exists in the project).</summary>
    [HttpPost("project-memory")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> CreateProjectMemoryDefinitionAsync(
        [FromBody] SaveAgentRequestDto body,
        CancellationToken cancellationToken = default)
    {
        var specId = body.Spec?.Id?.Trim();
        if (string.IsNullOrEmpty(specId))
            return BadRequest(new { error = "spec.id is required." });

        var r = await _projectMemoryAgentYaml.SaveAgentAsync(specId, body, createOnly: true, cancellationToken).ConfigureAwait(false);
        return r.StatusCode switch
        {
            200 => Ok(r.Data),
            409 => Conflict(r.Error),
            _ => StatusCode(r.StatusCode, r.Error)
        };
    }

    /// <summary>Updates YAML on disk (writes default path for new ids).</summary>
    [HttpPut("project-memory/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UpdateProjectMemoryDefinitionAsync(
        string id,
        [FromBody] SaveAgentRequestDto body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "id required." });

        var r = await _projectMemoryAgentYaml.SaveAgentAsync(id, body, createOnly: false, cancellationToken).ConfigureAwait(false);
        return r.StatusCode switch
        {
            200 => Ok(r.Data),
            _ => StatusCode(r.StatusCode, r.Error)
        };
    }

    /// <summary>Deletes the backing YAML file.</summary>
    [HttpDelete("project-memory/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteProjectMemoryDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "id required." });

        var r = await _projectMemoryAgentYaml.DeleteAgentAsync(id, cancellationToken).ConfigureAwait(false);
        return r.StatusCode switch
        {
            200 => Ok(r.Data),
            404 => NotFound(r.Error),
            _ => StatusCode(r.StatusCode, r.Error)
        };
    }
}
