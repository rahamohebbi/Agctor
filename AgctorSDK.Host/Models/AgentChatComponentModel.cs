namespace AgctorSDK.Host.Models;

/// <summary>
/// Configures the agent chat panel on dashboard pages.
/// </summary>
public class AgentChatComponentModel
{
    public string ComponentId { get; set; } = "agent-chat";
    public string Title { get; set; } = "Chat with agents";
    public string HelpText { get; set; } = "query-agent answers questions about indexed code. coder-agent: natural-language prompts are planned by refactor-agent then applied by coder-agent. refactor-agent: refactors. Click Index before code questions.";
}
