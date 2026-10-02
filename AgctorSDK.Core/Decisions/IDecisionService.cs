using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Runs route, call, escalation, and fallback. The decision actor is the runtime entry; this is what that actor hosts.
    /// </summary>
    public interface IDecisionService
    {
        Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default);

        Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionContext context, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Hook for later memory, analytics, or outcome tracking. The actor call site stays <c>Context.Decide</c>.
    /// </summary>
    public interface IDecisionObserver
    {
        Task OnCompletedAsync(DecisionRequest request, DecisionResult result, CancellationToken cancellationToken = default);
    }
}
