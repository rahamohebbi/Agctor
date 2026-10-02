using System.Threading.Tasks;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Picks one plugin for a request. Selection is deterministic given the same providers, policy, and context.
    /// </summary>
    public interface IDecisionRouter
    {
        Task<IDecisionProvider> SelectProviderAsync(DecisionRequest request, DecisionContext context);
    }
}
