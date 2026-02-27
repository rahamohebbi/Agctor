using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Resolves agent detail providers by agent type name and returns generic capabilities when no provider matches (PRD-006).
/// </summary>
public class AgentDetailProviderRegistry : IAgentDetailProviderRegistry
{
    private readonly IReadOnlyList<IAgentDetailProvider> _providers;

    public AgentDetailProviderRegistry(IEnumerable<IAgentDetailProvider> providers)
    {
        _providers = providers?.ToList() ?? new List<IAgentDetailProvider>();
    }

    public object? GetDetail(IAgent agent)
    {
        if (agent == null) return null;
        var typeName = agent.GetType().Name;
        var provider = _providers.FirstOrDefault(p => string.Equals(p.AgentTypeName, typeName, StringComparison.OrdinalIgnoreCase));
        if (provider != null)
            return provider.GetDetail(agent);
        return GetGenericDetail(agent);
    }

    private static object GetGenericDetail(IAgent agent)
    {
        var capabilities = new List<string> { "message-processing" };
        var agentType = agent.GetType();
        if (agentType.GetInterfaces().Any(i => i.Name.Contains("Tool")))
            capabilities.Add("tool-usage");
        return new { capabilities };
    }
}
