using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Tools;

/// <summary>
/// File-backed operations used by project-memory agents (read_document, write_document, load_schema, search_entities).
/// </summary>
public sealed class ProjectMemoryOperations
{
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;

    public ProjectMemoryOperations(IProjectLoader loader, IEntityRegistry entities)
    {
        _loader = loader;
        _entities = entities;
    }

    public async Task<string> ReadDocumentAsync(
        AgentDefinitionSpec spec,
        string projectRoot,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (!ProjectMemoryAccessGuard.CanRead(spec, relativePath))
            throw new UnauthorizedAccessException($"read denied for {relativePath}");

        var full = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        var root = Path.GetFullPath(projectRoot);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("path outside project root");

        if (!File.Exists(full))
            return "";
        return await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteDocumentAsync(
        AgentDefinitionSpec spec,
        string projectRoot,
        string relativePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (!ProjectMemoryAccessGuard.CanWrite(spec, relativePath))
            throw new UnauthorizedAccessException($"write denied for {relativePath}");

        var full = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        var root = Path.GetFullPath(projectRoot);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("path outside project root");

        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(full, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> LoadSchemaAsync(
        AgentDefinitionSpec spec,
        string projectRoot,
        string relativePathUnderAgctor,
        CancellationToken cancellationToken = default)
    {
        var rel = relativePathUnderAgctor.Replace('\\', '/').TrimStart('/');
        var full = Path.Combine(projectRoot, ".agctor", rel.Replace('/', Path.DirectorySeparatorChar));
        var logical = Path.Combine(".agctor", rel).Replace('\\', '/');
        if (!ProjectMemoryAccessGuard.CanRead(spec, logical))
            throw new UnauthorizedAccessException($"schema read denied for {logical}");

        if (!File.Exists(full))
            return "";
        return await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntitySearchHit>> SearchEntitiesAsync(
        string projectRoot,
        string? query,
        CancellationToken cancellationToken = default)
    {
        var ctx = await _loader.LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var list = await _entities.DiscoverAsync(ctx, cancellationToken).ConfigureAwait(false);
        var q = query?.Trim() ?? "";
        var hits = new List<EntitySearchHit>();
        foreach (var e in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(q) ||
                e.EntityKey.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Metadata.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(new EntitySearchHit(e.EntityKey, e.EntityType, e.RootPath));
            }
        }

        return hits;
    }
}

public sealed record EntitySearchHit(string EntityKey, string EntityType, string RootPath);
