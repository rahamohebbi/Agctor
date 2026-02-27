namespace AgctorSDK.Core.Interfaces;

/// <summary>
/// Resolves the appropriate detail provider for an agent and returns the detail payload (PRD-006).
/// </summary>
public interface IAgentDetailProviderRegistry
{
    /// <summary>
    /// Gets the detail payload for the agent using the first provider that matches agent.GetType().Name.
    /// Returns null or a generic payload if no provider is registered for that type.
    /// </summary>
    object? GetDetail(IAgent agent);
}
