using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory;

/// <summary>
/// Rebuildable SQLite/Postgres acceleration layer (PRD §15).
/// </summary>
public interface IRuntimeIndexStore : IAsyncDisposable
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task RebuildProjectAsync(
        LoadedProjectContext ctx,
        IReadOnlyList<EntityRecord> entities,
        IDocumentParser parser,
        CancellationToken cancellationToken = default);
}
