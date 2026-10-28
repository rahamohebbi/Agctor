using System.ComponentModel.DataAnnotations;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>Small manual Scene Memory API and an optional, deterministic agent test path.</summary>
[ApiController]
[Route("api/scene-memory")]
public sealed class SceneMemoryController(ISceneMemoryStore store, ISceneMemoryProvider provider, IActorRuntimeAdapter runtime,
    IAgentFactory factory, IAgentRegistry registry) : ControllerBase
{
    /// <summary>Readiness probe with the same response shape as the RAG provider health endpoint.</summary>
    [HttpGet("health")]
    public async Task<ActionResult<RagProviderHealthResponseDto>> Health(CancellationToken ct) =>
        Ok(ProviderHealthResponseMapper.Map(provider.ProviderId, await provider.GetHealthAsync(ct)));

    /// <summary>Provider-level query with common outcome fields and a typed scene result.</summary>
    [HttpPost("query")]
    public async Task<ActionResult<SceneMemoryProviderQueryResponseDto>> ProviderQuery(SceneMemoryQueryRequest body, CancellationToken ct)
    {
        try
        {
            var result = await provider.QueryAsync(body, ct);
            return Ok(new SceneMemoryProviderQueryResponseDto
            {
                Success = true, ProviderId = provider.ProviderId, Message = result.Answer, Result = result
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new SceneMemoryProviderQueryResponseDto { ProviderId = provider.ProviderId, Message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new SceneMemoryProviderQueryResponseDto { ProviderId = provider.ProviderId, Message = ex.Message });
        }
    }

    /// <summary>Lists workspaces without loading their full history into the response.</summary>
    [HttpGet("workspaces")]
    public async Task<IActionResult> List(CancellationToken ct) => Ok((await store.ListAsync(ct)).Select(w =>
        new { w.Id, w.Name, w.CreatedAt, entityCount = w.Entities.Count, changeCount = w.History.Count }));

    /// <summary>Creates a blank workspace.</summary>
    [HttpPost("workspaces")]
    public Task<IActionResult> Create(NameRequest body, CancellationToken ct) => Execute(async () => Ok(await store.CreateAsync(body.Name, ct)));

    /// <summary>Gets manually entered entities and their change history.</summary>
    [HttpGet("workspaces/{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct) => Execute(async () => Ok(await store.GetAsync(id, ct)));

    /// <summary>Adds an entity identity.</summary>
    [HttpPost("workspaces/{id}/entities")]
    public Task<IActionResult> AddEntity(string id, EntityRequest body, CancellationToken ct) =>
        Execute(async () => Ok(await store.AddEntityAsync(id, body.Name, body.Kind, ct)));

    /// <summary>Records an explicit property value and optional explanatory event note.</summary>
    [HttpPost("workspaces/{id}/facts")]
    public Task<IActionResult> Record(string id, FactRequest body, CancellationToken ct) =>
        Execute(async () => Ok(await store.RecordAsync(id, body.EntityId, body.Property, body.Value, body.Note, ct)));

    /// <summary>Direct retrieval. Does not create an actor or call an AI model.</summary>
    [HttpPost("workspaces/{id}/query")]
    public Task<IActionResult> Query(string id, SceneQuery body, CancellationToken ct) =>
        Execute(async () => Ok(await provider.QueryAsync(new SceneMemoryQueryRequest(id, body.Question, body.EntityId, body.AtSequence), ct)));

    /// <summary>Lists live test agents attached to a workspace.</summary>
    [HttpGet("workspaces/{id}/agents")]
    public Task<IActionResult> Agents(string id, CancellationToken ct) => Execute(async () =>
    {
        await store.GetAsync(id, ct);
        return Ok((await registry.GetAllAgentsAsync()).OfType<SceneMemoryTestAgent>().Where(a => a.WorkspaceId == id)
            .Select(a => new { a.Id, a.Name, a.WorkspaceId }));
    });

    /// <summary>Creates a real runtime agent with a fixed Scene Memory workspace. Lives until Host/runtime restart.</summary>
    [HttpPost("workspaces/{id}/agents")]
    public Task<IActionResult> CreateAgent(string id, NameRequest body, CancellationToken ct) => Execute(async () =>
    {
        await store.GetAsync(id, ct);
        var name = body.Name.Trim();
        if (name.Length == 0) throw new ArgumentException("Agent name is required.");
        var agentId = "scene-memory-test-" + Guid.NewGuid().ToString("N");
        var agent = await runtime.SpawnActorAsync(agentId, key => new SceneMemoryTestAgent(key, name, id, factory), cancellationToken: ct);
        await registry.RegisterAgentAsync(agent);
        return Ok(new { agent.Id, agent.Name, agent.WorkspaceId });
    });

    /// <summary>Runs retrieval through agent → tool → Scene Memory, returning the same facts as direct queries.</summary>
    [HttpPost("workspaces/{id}/agents/{agentId}/query")]
    public Task<IActionResult> AgentQuery(string id, string agentId, SceneQuery body, CancellationToken ct) => Execute(async () =>
    {
        var agent = await registry.GetAgentAsync<SceneMemoryTestAgent>(agentId);
        if (agent == null || agent.WorkspaceId != id) throw new KeyNotFoundException("Test agent not found in this workspace. Create one after a Host restart.");
        var result = await runtime.SendMessageAsync<ToolResult>(agentId, body, TimeSpan.FromSeconds(30), cancellationToken: ct);
        if (!result.IsSuccess) return BadRequest(new { error = result.Error });
        return Ok(new { agentId, tool = "scene-memory-query", result = result.Output });
    });

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    /// <summary>Workspace or test-agent display name.</summary>
    public sealed record NameRequest([Required, StringLength(100)] string Name);
    /// <summary>Manual entity identity.</summary>
    public sealed record EntityRequest([Required, StringLength(100)] string Name, [Required] string Kind);
    /// <summary>Explicit property change with an optional human-authored note.</summary>
    public sealed record FactRequest([Required] string EntityId, [Required, StringLength(100)] string Property,
        [Required, StringLength(1000)] string Value, [StringLength(2000)] string? Note);
}
