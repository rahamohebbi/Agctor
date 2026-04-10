using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory;

public interface IDocumentProjectionService
{
    /// <summary>Apply routed intents to canonical markdown files for one entity.</summary>
    Task<ProjectionResult> ApplyAsync(
        EntityRecord entity,
        IReadOnlyList<RoutedMemoryIntent> intents,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectionResult
{
    public List<string> UpdatedFiles { get; } = new();
    public List<ValidationIssue> Issues { get; } = new();
}
