using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>Process-local Scene Memory model benchmarks. Evaluation scenes never enter the durable workspace store.</summary>
[ApiController]
[Route("api/scene-memory/evaluations")]
public sealed class SceneMemoryEvaluationsController(SceneMemoryEvaluationService evaluations) : ControllerBase
{
    [HttpGet("catalog")]
    public IActionResult Catalog() => Ok(new
    {
        count = SceneMemoryEvaluationService.Catalog.Count,
        categories = SceneMemoryEvaluationService.Catalog.GroupBy(c => c.Category).Select(g => new { name = g.Key, count = g.Count() }),
        cases = SceneMemoryEvaluationService.Catalog
    });

    [HttpPost]
    public IActionResult Start(SceneEvaluationStartRequest body) => Execute(() => Ok(evaluations.Start(body)));

    [HttpGet("{id}")]
    public IActionResult Get(string id) => Execute(() => Ok(evaluations.Get(id)));

    [HttpPost("{id}/cancel")]
    public IActionResult Cancel(string id) => Execute(() => Ok(evaluations.Cancel(id)));

    private IActionResult Execute(Func<IActionResult> action)
    {
        try { return action(); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}
