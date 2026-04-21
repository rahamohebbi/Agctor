using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory.Resolution.Actors;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Review;

/// <summary>
/// Façade that a Host controller can depend on to accept review actions without learning the
/// actor addressing scheme. Each call becomes a <c>PromotionRequested</c> / <c>DemotionRequested</c>
/// message sent to the owning resolution actor via the runtime adapter.
/// </summary>
public sealed class ResolutionReviewService
{
    private readonly IActorRuntimeAdapter _runtime;
    private readonly IResolutionActorAddressing _addressing;
    private readonly string _projectId;

    public ResolutionReviewService(IActorRuntimeAdapter runtime, IResolutionActorAddressing addressing, string projectId)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _addressing = addressing ?? throw new ArgumentNullException(nameof(addressing));
        _projectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
    }

    public Task PromoteAsync(string targetEntityKey, string edgeId, string requestedBy, string? reason, CancellationToken ct = default)
    {
        var actorId = _addressing.ActorIdFor(_projectId, targetEntityKey);
        return _runtime.SendMessageAsync(actorId, new PromotionRequested
        {
            EdgeId = edgeId,
            RequestedBy = requestedBy,
            Reason = reason
        }, senderId: "review-service", cancellationToken: ct);
    }

    public Task DemoteAsync(string targetEntityKey, string edgeId, string requestedBy, string? reason, CancellationToken ct = default)
    {
        var actorId = _addressing.ActorIdFor(_projectId, targetEntityKey);
        return _runtime.SendMessageAsync(actorId, new DemotionRequested
        {
            EdgeId = edgeId,
            RequestedBy = requestedBy,
            Reason = reason,
            Reject = false
        }, senderId: "review-service", cancellationToken: ct);
    }

    public Task RejectAsync(string targetEntityKey, string edgeId, string requestedBy, string? reason, CancellationToken ct = default)
    {
        var actorId = _addressing.ActorIdFor(_projectId, targetEntityKey);
        return _runtime.SendMessageAsync(actorId, new DemotionRequested
        {
            EdgeId = edgeId,
            RequestedBy = requestedBy,
            Reason = reason,
            Reject = true
        }, senderId: "review-service", cancellationToken: ct);
    }
}
