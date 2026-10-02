namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Stable names shared by routing, configuration, and the decision actor.
    /// </summary>
    public static class DecisionFabricIds
    {
        public const string Rules = "rules";
        public const string Laya = "laya";
        public const string OpenAI = "openai";

        /// <summary>
        /// Well-known id of the actor that owns decision requests.
        /// Callers never address a provider; they address this actor through <see cref="IActorContext"/>.
        /// </summary>
        public const string DecisionActorId = "agctor.decisions";
    }
}
