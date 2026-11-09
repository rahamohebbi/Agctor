using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.VideoWorkflow;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

[ApiController]
public sealed class AgentRunsController(SceneVideoScenario scenario, IAgentRunStore runs, ISceneVideoRenderer renderer, ISceneVideoStoryGateway stories) : ControllerBase
{
    public sealed record ReplyRequest(long Revision, string QuestionId, string? Choice, string? Text);
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
    public async Task<IActionResult> Graph(string id, CancellationToken ct) =>
        await runs.GetAsync(id, ct) == null ? NotFound() : Ok(await runs.GraphAsync(id, ct));
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
