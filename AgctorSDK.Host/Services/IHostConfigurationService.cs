using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Provides aggregated Host configuration for the dashboard (PRD-006).
/// </summary>
public interface IHostConfigurationService
{
    /// <summary>
    /// Returns full Host configuration: runtime, LLM, MCP, paths, background services, agent types, tools, scenarios.
    /// </summary>
    Task<HostConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default);
}
