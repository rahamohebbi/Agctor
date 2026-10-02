using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Deterministic router:
    /// rules when they can answer, Laya for simple choice/score/binary, LLM when the request is complex
    /// or the policy asks for accuracy. Policy filters (local, cost, latency, allow/block) run first.
    /// Escalation sets <see cref="DecisionContext.Escalate"/> so the next pick is the LLM.
    /// </summary>
    public sealed class DecisionRouter : IDecisionRouter
    {
        private readonly IReadOnlyList<IDecisionProvider> _providers;
        private readonly DecisionFabricOptions _options;

        public DecisionRouter(IEnumerable<IDecisionProvider> providers, DecisionFabricOptions? options = null)
        {
            _providers = (providers ?? Array.Empty<IDecisionProvider>()).Where(provider => provider != null).ToList();
            _options = options ?? new DecisionFabricOptions();
        }

        public Task<IDecisionProvider> SelectProviderAsync(DecisionRequest request, DecisionContext context)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            context ??= new DecisionContext();
            var policy = request.Policy ?? context.Policy;
            var eligible = Eligible(policy, context).ToList();
            if (eligible.Count == 0)
            {
                throw new DecisionException("No decision provider is eligible for this request.", request.Id);
            }

            // Escalation already has an answer; it must move to the LLM rather than stay on rules or Laya.
            if (!context.Escalate)
            {
                var rules = eligible.FirstOrDefault(provider =>
                    provider.Profile.Role == DecisionEngineRole.Rules && provider.CanHandle(request));
                if (rules != null)
                {
                    return Task.FromResult(rules);
                }
            }

            var complex = context.Escalate
                || (policy?.PreferAccurate ?? false)
                || DecisionSignals.IsComplex(request, _options.Routing);

            if (!complex)
            {
                var laya = eligible.FirstOrDefault(provider =>
                    provider.Profile.Role == DecisionEngineRole.Laya && provider.CanHandle(request));
                if (laya != null)
                {
                    return Task.FromResult(laya);
                }
            }

            var llm = eligible.FirstOrDefault(provider =>
                provider.Profile.Role == DecisionEngineRole.Llm && provider.CanHandle(request));
            if (llm != null)
            {
                return Task.FromResult(llm);
            }

            foreach (var provider in Rank(eligible, policy))
            {
                if (provider.CanHandle(request))
                {
                    return Task.FromResult(provider);
                }
            }

            throw new DecisionException("No decision provider can handle this request.", request.Id);
        }

        private IEnumerable<IDecisionProvider> Eligible(DecisionPolicy? policy, DecisionContext context)
        {
            foreach (var provider in _providers)
            {
                var profile = provider.Profile;
                if (!profile.Enabled || !provider.IsAvailable)
                {
                    continue;
                }

                if (IsExcluded(context, provider.Name))
                {
                    continue;
                }

                if (policy != null && !PolicyAllows(policy, provider))
                {
                    continue;
                }

                yield return provider;
            }
        }

        private static bool PolicyAllows(DecisionPolicy policy, IDecisionProvider provider)
        {
            var profile = provider.Profile;
            if (policy.RequireLocal && !profile.IsLocal)
            {
                return false;
            }

            if (policy.BlockedProviders != null &&
                policy.BlockedProviders.Any(name => string.Equals(name, provider.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (policy.AllowedProviders is { Count: > 0 } &&
                !policy.AllowedProviders.Any(name => string.Equals(name, provider.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (policy.MaxCost.HasValue && profile.ExpectedCost > policy.MaxCost.Value)
            {
                return false;
            }

            var budget = policy.LatencyBudget;
            if (budget.HasValue && profile.ExpectedLatency > budget.Value)
            {
                return false;
            }

            return true;
        }

        private IEnumerable<IDecisionProvider> Rank(IReadOnlyList<IDecisionProvider> providers, DecisionPolicy? policy)
        {
            if (policy?.PreferAccurate == true)
            {
                return providers.OrderByDescending(provider => provider.Profile.ExpectedAccuracy);
            }

            if (policy?.PreferCheap == true)
            {
                return providers.OrderBy(provider => provider.Profile.ExpectedCost);
            }

            if (policy?.PreferFast == true)
            {
                return providers.OrderBy(provider => provider.Profile.ExpectedLatency);
            }

            if (policy?.PreferLocal == true)
            {
                return providers.OrderByDescending(provider => provider.Profile.IsLocal);
            }

            return providers.OrderByDescending(provider =>
                string.Equals(provider.Name, _options.DefaultProvider, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsExcluded(DecisionContext context, string name)
        {
            return context.ExcludedProviders != null &&
                context.ExcludedProviders.Any(excluded => string.Equals(excluded, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
