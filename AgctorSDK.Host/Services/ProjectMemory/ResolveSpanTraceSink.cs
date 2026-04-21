using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Resolution.Trace;
using AgctorSDK.Core.Utils.ActivityTracking;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Host implementation of <see cref="IResolveSpanSink"/>: pushes every resolved candidate onto the
/// activity tracker as a <c>pm.playground.resolve</c> span. The existing trace timeline UI already
/// knows how to surface spans whose <c>timelineDetailJson</c> carries the
/// <see cref="PlaygroundTraceTimelineDetail.BuildResolveJson"/> shape (PRD-018 §5.7 U1).
/// </summary>
public sealed class ResolveSpanTraceSink : IResolveSpanSink
{
    private readonly IActivityTracker? _tracker;

    public ResolveSpanTraceSink(IActivityTracker? tracker)
    {
        _tracker = tracker;
    }

    public Task EmitAsync(ResolveSpanDetail detail, CancellationToken cancellationToken = default)
    {
        if (_tracker == null || detail == null) return Task.CompletedTask;

        using var scope = _tracker.StartActivity("pm.playground.resolve");
        scope.SetAttribute("display.name", $"resolve {detail.Input.SurfaceForm} -> {detail.Outcome.State}");
        scope.SetAttribute("edge.id", detail.Outcome.EdgeId ?? "");
        scope.SetAttribute("edge.state", detail.Outcome.State ?? "");
        scope.SetAttribute("confidence", detail.Evidence.Confidence.ToString("F3"));
        scope.SetTimelineDetailJson(PlaygroundTraceTimelineDetail.BuildResolveJson(detail));
        scope.SetStatus(ActivityStatus.Ok);
        return Task.CompletedTask;
    }
}
