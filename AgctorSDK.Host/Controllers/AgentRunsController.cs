using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

[ApiController]
public sealed class AgentRunsController(
    SceneVideoScenario scenario,
    IAgentRunStore runs,
    ISceneVideoRenderer renderer,
    ISceneVideoStoryGateway stories,
    IScenarioCatalog catalog) : ControllerBase
{
    public sealed record ReplyRequest(long Revision, string QuestionId, string? Choice, string? Text);

    /// <summary>
    /// Live graph: conversation-flow skeleton only; observed activity remaps onto those nodes.
    /// <see cref="Flow"/> is the sole definition surface (edit canvas) — never a second observed topology.
    /// </summary>
    /// <param name="Layouts">Canonical-id positions from flow.ui.nodeLayouts for Agctor/Cynelo live renderers.</param>
    public sealed record GraphResponse(
        IReadOnlyList<AgentGraphNode> Nodes,
        IReadOnlyList<AgentGraphEdge> Edges,
        IReadOnlyList<AgentActiveCall>? ActiveCalls,
        ScenarioFlowDocument? Flow,
        Dictionary<string, ScenarioFlowNodeLayout>? Layouts);

    [HttpPost("api/scenarios/{scenarioId}/runs")]
    public Task<IActionResult> Start(string scenarioId, StartSceneVideoRun request, CancellationToken ct) => Guard(async () =>
    {
        var run = await scenario.StartAsync(scenarioId, request, ct);
        return Accepted($"/api/agent-runs/{run.Id}", run);
    });
    [HttpGet("api/agent-runs")]
    public async Task<IActionResult> List([FromQuery] string? scenarioId, CancellationToken ct) => Ok(await runs.ListAsync(scenarioId, ct));
    [HttpGet("api/agent-runs/connection")]
    public IActionResult Connection() => Ok(new { rendererConfigured = renderer.IsConfigured });
    [HttpGet("api/agent-runs/stories")]
    public Task<IActionResult> Stories(CancellationToken ct) => Guard(async () => Ok(await stories.ListAsync(ct)));
    [HttpGet("api/agent-runs/{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct) => await runs.GetAsync(id, ct) is { } run ? Ok(run) : NotFound();
    [HttpGet("api/agent-runs/{id}/events")]
    public async Task<IActionResult> Events(string id, [FromQuery] long after, CancellationToken ct) =>
        await runs.GetAsync(id, ct) == null ? NotFound() : Ok(await runs.EventsAsync(id, Math.Max(0, after), ct));
    [HttpGet("api/agent-runs/{id}/graph")]
    public async Task<IActionResult> Graph(string id, CancellationToken ct)
    {
        var run = await runs.GetAsync(id, ct);
        if (run == null) return NotFound();
        var observed = await runs.GraphAsync(id, ct);
        var flow = catalog.Get(run.ScenarioId)?.Flow;
        var projection = ScenarioFlowGraphProjector.Project(flow);
        if (projection == null)
            return Ok(new GraphResponse(observed.Nodes, observed.Edges, observed.ActiveCalls, flow, null));

        var composed = ScenarioFlowGraphProjector.Compose(projection, observed);
        var layouts = projection.Layouts.ToDictionary(
            kv => kv.Key,
            kv => new ScenarioFlowNodeLayout { X = kv.Value.X, Y = kv.Value.Y },
            StringComparer.Ordinal);
        return Ok(new GraphResponse(composed.Nodes, composed.Edges, composed.ActiveCalls, projection.Flow, layouts));
    }
    [HttpPost("api/agent-runs/{id}/reply")]
    public Task<IActionResult> Reply(string id, ReplyRequest request, CancellationToken ct) => Guard(async () =>
        Ok(await runs.ReplyAsync(id, request.Revision, new(request.QuestionId, request.Choice, request.Text), ct)));
    [HttpPost("api/agent-runs/{id}/cancel")]
    public Task<IActionResult> Cancel(string id, CancellationToken ct) => Guard(async () => Ok(await runs.CancelAsync(id, ct)));
    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (SceneRenderInterventionException ex) { return StatusCode(503, new { error = ex.Message }); }
        catch (HttpRequestException) { return StatusCode(503, new { error = "Could not reach Cynelo. Check the connection and try again." }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}
