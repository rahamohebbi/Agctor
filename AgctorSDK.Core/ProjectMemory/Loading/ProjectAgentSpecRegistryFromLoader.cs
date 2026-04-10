using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.ProjectMemory.Loading;

/// <summary>
/// Exposes agent YAML specs from the current <see cref="ProjectMemoryAgentOptions.ProjectRoot"/> via <see cref="IProjectLoader"/>.
/// Registered in <see cref="DependencyInjection.ProjectMemoryServiceExtensions.AddAgctorProjectMemory"/> for Host/dashboard use.
/// </summary>
public sealed class ProjectAgentSpecRegistryFromLoader : IProjectAgentSpecRegistry
{
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions> _options;
    private readonly IProjectLoader _loader;
    private readonly ILogger<ProjectAgentSpecRegistryFromLoader> _logger;

    public ProjectAgentSpecRegistryFromLoader(
        IOptionsMonitor<ProjectMemoryAgentOptions> options,
        IProjectLoader loader,
        ILogger<ProjectAgentSpecRegistryFromLoader> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentDefinitionSpec>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var root = _options.CurrentValue.ProjectRoot?.Trim();
        if (string.IsNullOrEmpty(root))
            return Array.Empty<AgentDefinitionSpec>();

        try
        {
            var ctx = await _loader.LoadAsync(Path.GetFullPath(root), cancellationToken).ConfigureAwait(false);
            return ctx.AgentSpecs;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load agent specs for project root {Root}", root);
            return Array.Empty<AgentDefinitionSpec>();
        }
    }

    /// <inheritdoc />
    public async Task<AgentDefinitionSpec?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentDefinitionSpec>> GetByProjectTypeAsync(
        string projectType,
        CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Where(a => a.ProjectTypes.Any(p => string.Equals(p, projectType, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
