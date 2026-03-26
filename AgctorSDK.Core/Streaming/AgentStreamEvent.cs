namespace AgctorSDK.Core.Streaming
{
    /// <summary>
    /// One server-sent event payload for agent/LLM streaming (PRD-011).
    /// </summary>
    public sealed class AgentStreamEvent
    {
        public string Type { get; set; } = "";

        public string? Payload { get; set; }

        public string? TraceId { get; set; }

        public string? AgentId { get; set; }
    }
}
