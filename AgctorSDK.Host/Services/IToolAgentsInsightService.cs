using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>Builds a merged view of host tools and which agents are associated (YAML allow lists + C# hints).</summary>
public interface IToolAgentsInsightService
{
    Task<ToolAgentsInsightResponse> GetInsightAsync(CancellationToken cancellationToken = default);

    /// <summary>Same underlying data as <see cref="GetInsightAsync"/> grouped by agent for dashboards.</summary>
    Task<AgentToolsInsightResponse> GetAgentsToolInsightAsync(CancellationToken cancellationToken = default);
}
