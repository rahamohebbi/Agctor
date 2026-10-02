using System;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Raised when the fabric cannot produce a decision. Provider failures that still have a fallback do not escape.
    /// </summary>
    public class DecisionException : Exception
    {
        public DecisionException(string message, string? decisionId = null, string? provider = null, Exception? inner = null)
            : base(message, inner)
        {
            DecisionId = decisionId;
            Provider = provider;
        }

        public string? DecisionId { get; }
        public string? Provider { get; }
    }
}
