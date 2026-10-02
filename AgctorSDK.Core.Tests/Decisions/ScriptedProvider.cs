using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Decisions;

namespace AgctorSDK.Core.Tests.Decisions
{
    /// <summary>
    /// In-memory provider so routing tests never call a live model endpoint.
    /// </summary>
    internal sealed class ScriptedProvider : IDecisionProvider
    {
        private readonly Func<DecisionRequest, DecisionResult>? _decide;

        public ScriptedProvider(
            string name,
            DecisionEngineRole role,
            bool local,
            int latencyMs,
            decimal cost,
            double accuracy,
            Func<DecisionRequest, DecisionResult>? decide = null)
        {
            Name = name;
            Profile = new DecisionProviderProfile
            {
                Name = name,
                Role = role,
                IsLocal = local,
                ExpectedLatency = TimeSpan.FromMilliseconds(latencyMs),
                ExpectedCost = cost,
                ExpectedAccuracy = accuracy,
                Enabled = true
            };
            _decide = decide;
        }

        public string Name { get; }
        public DecisionProviderProfile Profile { get; }
        public bool IsAvailable { get; set; } = true;
        public Exception? Error { get; set; }
        public int Calls { get; private set; }
        public Func<DecisionRequest, bool>? Handle { get; set; }

        public bool CanHandle(DecisionRequest request)
        {
            return IsAvailable && (Handle?.Invoke(request) ?? true);
        }

        public async Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Error != null)
            {
                throw Error;
            }

            await Task.Delay(10, cancellationToken);
            var result = _decide?.Invoke(request) ?? new DecisionResult
            {
                Value = request.Type == DecisionType.Binary ? true : request.Type == DecisionType.Score ? 80d : "call",
                Confidence = 0.9,
                Cost = Profile.ExpectedCost
            };
            result.Provider = Name;
            result.DecisionId = request.Id;
            return result;
        }
    }
}
