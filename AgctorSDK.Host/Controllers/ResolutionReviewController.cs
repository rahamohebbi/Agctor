using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Resolution;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using AgctorSDK.Core.ProjectMemory.Resolution.Observability;
using AgctorSDK.Core.ProjectMemory.Resolution.Review;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// PRD-018 §5.7 U3: HTTP surface the dashboard and CLIs use to inspect pending soft links and
/// promote, demote, or reject them. All mutations go through the resolution actor's mailbox via
/// <see cref="ResolutionReviewService"/> so invariants (single-writer edges, append-only evidence)
/// stay with the actor rather than leaking into controllers.
/// </summary>
[ApiController]
[Route("api/project-memory/resolution")]
[Produces("application/json")]
public sealed class ResolutionReviewController : ControllerBase
{
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions> _options;
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;
    private readonly IActorRuntimeAdapter _runtime;
    private readonly IResolutionActorAddressing _addressing;
    private readonly ResolutionBootstrapper _bootstrapper;
    private readonly ResolutionMetrics _metrics;

    public ResolutionReviewController(
        IOptionsMonitor<ProjectMemoryAgentOptions> options,
        IProjectLoader loader,
        IEntityRegistry entities,
        IActorRuntimeAdapter runtime,
        IResolutionActorAddressing addressing,
        ResolutionBootstrapper bootstrapper,
        ResolutionMetrics metrics)
    {
        _options = options;
        _loader = loader;
        _entities = entities;
        _runtime = runtime;
        _addressing = addressing;
        _bootstrapper = bootstrapper;
        _metrics = metrics;
    }

    private string? RootOrNull()
    {
        var r = _options.CurrentValue.ProjectRoot?.Trim();
        return string.IsNullOrEmpty(r) ? null : System.IO.Path.GetFullPath(r);
    }

    /// <summary>List pending soft links for review, ranked by confidence × recency.</summary>
    [HttpGet("pending")]
    public async Task<ActionResult<PendingReviewResponse>> Pending(
        [FromQuery] int max = 50,
        [FromQuery] double minConfidence = 0,
        CancellationToken cancellationToken = default)
    {
        var root = RootOrNull();
        if (root == null) return BadRequest(new { error = "project root not configured" });

        var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
        var entities = await _entities.DiscoverAsync(ctx, cancellationToken).ConfigureAwait(false);
        var rows = new ResolutionReviewQuery(entities).Pending(max, minConfidence);
        return Ok(new PendingReviewResponse
        {
            ProjectId = _bootstrapper.ProjectId ?? ctx.Project.ProjectId,
            Rows = rows
        });
    }

    /// <summary>Operator confirms a soft link and promotes it to hard.</summary>
    [HttpPost("promote")]
    public async Task<IActionResult> Promote([FromBody] ReviewActionRequest body, CancellationToken cancellationToken = default)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.EntityKey) || string.IsNullOrWhiteSpace(body.EdgeId))
            return BadRequest(new { error = "entityKey and edgeId required" });
        var projectId = _bootstrapper.ProjectId ?? "default";
        var service = new ResolutionReviewService(_runtime, _addressing, projectId);
        await service.PromoteAsync(body.EntityKey!, body.EdgeId!, body.RequestedBy ?? "user:dashboard", body.Reason, cancellationToken);
        return Ok(new { ok = true });
    }

    /// <summary>Operator demotes a hard link back to soft.</summary>
    [HttpPost("demote")]
    public async Task<IActionResult> Demote([FromBody] ReviewActionRequest body, CancellationToken cancellationToken = default)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.EntityKey) || string.IsNullOrWhiteSpace(body.EdgeId))
            return BadRequest(new { error = "entityKey and edgeId required" });
        var projectId = _bootstrapper.ProjectId ?? "default";
        var service = new ResolutionReviewService(_runtime, _addressing, projectId);
        await service.DemoteAsync(body.EntityKey!, body.EdgeId!, body.RequestedBy ?? "user:dashboard", body.Reason, cancellationToken);
        return Ok(new { ok = true });
    }

    /// <summary>Operator rejects a soft link outright.</summary>
    [HttpPost("reject")]
    public async Task<IActionResult> Reject([FromBody] ReviewActionRequest body, CancellationToken cancellationToken = default)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.EntityKey) || string.IsNullOrWhiteSpace(body.EdgeId))
            return BadRequest(new { error = "entityKey and edgeId required" });
        var projectId = _bootstrapper.ProjectId ?? "default";
        var service = new ResolutionReviewService(_runtime, _addressing, projectId);
        await service.RejectAsync(body.EntityKey!, body.EdgeId!, body.RequestedBy ?? "user:dashboard", body.Reason, cancellationToken);
        return Ok(new { ok = true });
    }

    /// <summary>Dump metrics counters for dashboards / dogfood.</summary>
    [HttpGet("metrics")]
    public ActionResult<object> Metrics()
    {
        return Ok(new { counters = _metrics.Snapshot() });
    }
}

public sealed class ReviewActionRequest
{
    public string? EntityKey { get; set; }
    public string? EdgeId { get; set; }
    public string? RequestedBy { get; set; }
    public string? Reason { get; set; }
}

public sealed class PendingReviewResponse
{
    public string ProjectId { get; set; } = "";
    public System.Collections.Generic.IReadOnlyList<ResolutionReviewQuery.ReviewRow> Rows { get; set; } = System.Array.Empty<ResolutionReviewQuery.ReviewRow>();
}
