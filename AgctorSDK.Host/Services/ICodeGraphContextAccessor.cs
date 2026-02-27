using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Provides access to the current CodeGraph context when code-graph-demo scenario has been set up (PRD-006).
/// </summary>
public interface ICodeGraphContextAccessor
{
    /// <summary>
    /// Gets the current CodeGraph context (actor tree + embedding summary), or null if no CodeGraph scenario is active.
    /// </summary>
    CodeGraphContextDto? GetCurrent();

    /// <summary>
    /// Sets the current context. Called by CodeGraphDemoScenario after setup.
    /// </summary>
    void SetCurrent(CodeGraphContextDto? context);
}
