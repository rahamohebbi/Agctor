using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Chains person-extractor → <see cref="Processing.IMemoryIntentProcessor"/> → projection → optional person-query.
/// In-process (no actor mailbox) for predictable ordering; actors remain available for interactive use.
/// </summary>
public interface IProjectMemoryPipelineRunner
{
    Task<ProjectMemoryPipelineResult> RunAsync(ProjectMemoryPipelineRequest request, CancellationToken cancellationToken = default);
}
