using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Which kind of engine a plugin is. The router uses this so a renamed provider still follows the same path.
    /// </summary>
    public enum DecisionEngineRole
    {
        Rules = 0,
        Laya = 1,
        Llm = 2
    }

    /// <summary>
    /// Cost, latency, and locality estimates used by <see cref="DecisionPolicy"/>. These are routing hints, not bills.
    /// </summary>
    public class DecisionProviderProfile
    {
        public string Name { get; set; } = string.Empty;
        public DecisionEngineRole Role { get; set; }
        public bool IsLocal { get; set; }
        public TimeSpan ExpectedLatency { get; set; }
        public decimal ExpectedCost { get; set; }
        public double ExpectedAccuracy { get; set; }
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Plugin that turns a <see cref="DecisionRequest"/> into a provider-specific call and a normalized <see cref="DecisionResult"/>.
    /// Agents never reference an implementation directly.
    /// </summary>
    public interface IDecisionProvider
    {
        string Name { get; }
        DecisionProviderProfile Profile { get; }

        /// <summary>
        /// False when configuration or a health signal says this plugin must not be selected.
        /// A later call failure is still treated as unavailable and can fall back.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Whether this plugin can actually answer the request. Rules return true only for deterministic cases.
        /// </summary>
        bool CanHandle(DecisionRequest request);

        Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default);
    }
}
