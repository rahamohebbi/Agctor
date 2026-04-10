using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Typed access to the active project type schema bundle (resolved paths).
/// </summary>
public interface ISchemaRegistry
{
    ProjectTypeBundle Bundle { get; }
}
