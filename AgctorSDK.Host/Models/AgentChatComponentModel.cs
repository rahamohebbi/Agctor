namespace AgctorSDK.Host.Models;

/// <summary>
/// Configures the agent chat panel on dashboard pages.
/// </summary>
public class AgentChatComponentModel
{
    public string ComponentId { get; set; } = "agent-chat";
    public string Title { get; set; } = "Chat with agents";
    public string HelpText { get; set; } = "query-agent answers questions about indexed code. Use coder-agent to write or edit code, and refactor-agent for refactors. Click Index now before asking code questions.";
}
