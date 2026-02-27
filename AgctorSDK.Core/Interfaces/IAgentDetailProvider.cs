namespace AgctorSDK.Core.Interfaces;

/// <summary>
/// Provides a type-specific detail payload for an agent (dashboard / PRD-006).
/// Each agent type can have one provider that returns a custom shape for GET /api/agents/{id}/detail.
/// </summary>
public interface IAgentDetailProvider
{
    /// <summary>
    /// Agent type name this provider handles (e.g. "LLMAgent", "CoderAgent"). Matched to agent.GetType().Name.
    /// </summary>
    string AgentTypeName { get; }

    /// <summary>
    /// Returns a detail object for the agent. Shape is specific to the agent type; serialized as JSON in the API response.
    /// </summary>
    object? GetDetail(IAgent agent);
}
