using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// YAML agent role specs from <c>.agctor/agents/**/*.agent.yaml</c> (PRD AgentRegistry).
/// </summary>
public interface IProjectAgentSpecRegistry
{
    Task<IReadOnlyList<AgentDefinitionSpec>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<AgentDefinitionSpec?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentDefinitionSpec>> GetByProjectTypeAsync(string projectType, CancellationToken cancellationToken = default);
}
