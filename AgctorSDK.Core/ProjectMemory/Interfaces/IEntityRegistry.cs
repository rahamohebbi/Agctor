using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory;

public interface IEntityRegistry
{
    /// <summary>Discover and parse entities for the loaded project (canonical <c>people/</c> under <see cref="LoadedProjectContext.ProjectRoot"/>).</summary>
    Task<IReadOnlyList<EntityRecord>> DiscoverAsync(LoadedProjectContext ctx, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discover entities under <paramref name="entityWorkspaceRoot"/> (e.g. <c>{ProjectRoot}/scenarios/&lt;id&gt;/</c> containing <c>people/</c>).
    /// </summary>
    Task<IReadOnlyList<EntityRecord>> DiscoverAsync(
        LoadedProjectContext ctx,
        string? entityWorkspaceRoot,
        CancellationToken cancellationToken = default);
}

/// <summary>One canonical entity folder on disk.</summary>
public sealed class EntityRecord
{
    public string EntityKey { get; init; } = "";
    public string EntityType { get; init; } = "";
    public string RootPath { get; init; } = "";
    public EntityMetadata Metadata { get; init; } = new();
    public IReadOnlyList<string> DocumentRelativePaths { get; init; } = new List<string>();
}
