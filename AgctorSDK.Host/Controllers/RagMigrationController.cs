using AgctorSDK.Host.Services.Rag;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

[ApiController]
[Route("api/rag-migrations")]
public sealed class RagMigrationController(RagMigrationService migrations) : ControllerBase
{
    public sealed record PreviewRequest(string ScenarioId, string ProviderId, string Source = "scenario", bool Force = false);
    public sealed record StartRequest(string[] Paths);
    [HttpGet]
    public IActionResult List(string scenarioId) => Ok(migrations.List(scenarioId));
    [HttpGet("{id}")]
    public IActionResult Get(string id) => migrations.Get(id) is { } job ? Ok(job) : NotFound();
    [HttpGet("providers/{providerId}/readiness")]
    public async Task<IActionResult> Readiness(string providerId, CancellationToken ct)
    {
        try { return Ok(await migrations.CheckProviderAsync(providerId, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(PreviewRequest request, CancellationToken ct)
    {
        try { return Ok(await migrations.PreviewAsync(request.ScenarioId, request.ProviderId, request.Source, request.Force, ct)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return BadRequest(new { error = ex is ArgumentException or InvalidOperationException ? ex.Message : "Memory files could not be read. Check the configured Project Memory folder." }); }
    }
    [HttpPost("{id}/start")]
    public IActionResult Start(string id, StartRequest request)
    {
        try { return Accepted(value: migrations.Start(id, request.Paths)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
    [HttpPost("{id}/stop")]
    public IActionResult Stop(string id)
    {
        try { return Ok(migrations.Stop(id)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
