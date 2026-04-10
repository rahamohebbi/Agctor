using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory;

public interface IProjectLoader
{
    /// <summary>Load <c>.agctor/</c> under <paramref name="projectRoot"/> (folder that contains <c>.agctor</c>).</summary>
    Task<LoadedProjectContext> LoadAsync(string projectRoot, CancellationToken cancellationToken = default);
}
