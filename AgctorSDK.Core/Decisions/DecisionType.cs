namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// The three decision primitives the fabric knows how to route and normalize.
    /// </summary>
    public enum DecisionType
    {
        /// <summary>Pick one option from a list.</summary>
        Choice = 0,

        /// <summary>Score an item against a goal on a numeric range.</summary>
        Score = 1,

        /// <summary>Answer a yes/no question.</summary>
        Binary = 2
    }
}
