using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Per-actor gateway to the decision fabric.
    /// <see cref="Decide"/> sends a message to the decision actor; it does not call a provider.
    /// </summary>
    public interface IActorContext
    {
        string ActorId { get; }
        string? ActorType { get; }

        Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implemented by actors that can receive a context from the runtime at spawn time.
    /// </summary>
    public interface IHasActorContext
    {
        IActorContext? Context { get; }
        void BindContext(IActorContext context);
    }
}
