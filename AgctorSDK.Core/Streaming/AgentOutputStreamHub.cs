namespace AgctorSDK.Core.Streaming
{
    /// <summary>
    /// Global registry for streaming; Host sets this at startup so Core agents (LLMAgent) can publish without ctor injection.
    /// </summary>
    public static class AgentOutputStreamHub
    {
        public static IAgentOutputStreamRegistry Registry { get; set; } = NullAgentOutputStreamRegistry.Instance;
    }
}
