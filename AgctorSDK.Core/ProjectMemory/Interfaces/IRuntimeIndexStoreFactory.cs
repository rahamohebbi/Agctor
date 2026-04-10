using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Builds a store bound to one loaded project (path/connection come from manifest).
/// </summary>
public interface IRuntimeIndexStoreFactory
{
    IRuntimeIndexStore Create(LoadedProjectContext ctx);
}
