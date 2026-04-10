using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Loading;

public sealed class ProjectAgentSpecRegistry : IProjectAgentSpecRegistry
{
    private readonly IReadOnlyList<AgentDefinitionSpec> _specs;

    public ProjectAgentSpecRegistry(IReadOnlyList<AgentDefinitionSpec> specs)
    {
        _specs = specs;
    }

    public Task<IReadOnlyList<AgentDefinitionSpec>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_specs);

    public Task<AgentDefinitionSpec?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var s = _specs.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<AgentDefinitionSpec?>(s);
    }

    public Task<IReadOnlyList<AgentDefinitionSpec>> GetByProjectTypeAsync(string projectType, CancellationToken cancellationToken = default)
    {
        var list = _specs.Where(a => a.ProjectTypes.Any(p => string.Equals(p, projectType, StringComparison.OrdinalIgnoreCase))).ToList();
        return Task.FromResult<IReadOnlyList<AgentDefinitionSpec>>(list);
    }
}
