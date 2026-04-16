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
/// File-backed operations for project-memory agents. Logical <c>people/…</c> paths can map under <c>scenarios/&lt;id&gt;/</c> via optional workspace root.
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

    public Task<string> ReadDocumentAsync(
        AgentDefinitionSpec spec,
        string projectRoot,
        string relativePath,
        CancellationToken cancellationToken = default) =>
        ReadDocumentAsync(spec, projectRoot, null, relativePath, cancellationToken);

    public async Task<string> ReadDocumentAsync(
        AgentDefinitionSpec spec,
        string projectRoot,
        string? entityWorkspaceRoot,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (!ProjectMemoryAccessGuard.CanRead(spec, relativePath))
            throw new UnauthorizedAccessException($"read denied for {relativePath}");

        var full = ResolveDataFilePath(projectRoot, entityWorkspaceRoot, relativePath);
        if (!File.Exists(full))
            return "";
        return await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
    }

    public Task WriteDocumentAsync(
        AgentDefinitionSpec spec,
        string projectRoot,
        string relativePath,
        string content,
        CancellationToken cancellationToken = default) =>
        WriteDocumentAsync(spec, projectRoot, null, relativePath, content, cancellationToken);

    public async Task WriteDocumentAsync(
        AgentDefinitionSpec spec,
        string projectRoot,
        string? entityWorkspaceRoot,
        string relativePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (!ProjectMemoryAccessGuard.CanWrite(spec, relativePath))
            throw new UnauthorizedAccessException($"write denied for {relativePath}");

        var full = ResolveDataFilePath(projectRoot, entityWorkspaceRoot, relativePath);
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

    public Task<IReadOnlyList<EntitySearchHit>> SearchEntitiesAsync(
        string projectRoot,
        string? query,
        CancellationToken cancellationToken = default) =>
        SearchEntitiesAsync(projectRoot, null, query, cancellationToken);

    public async Task<IReadOnlyList<EntitySearchHit>> SearchEntitiesAsync(
        string projectRoot,
        string? entityWorkspaceRoot,
        string? query,
        CancellationToken cancellationToken = default)
    {
        var ctx = await _loader.LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var list = string.IsNullOrWhiteSpace(entityWorkspaceRoot)
            ? await _entities.DiscoverAsync(ctx, cancellationToken).ConfigureAwait(false)
            : await _entities.DiscoverAsync(ctx, Path.GetFullPath(entityWorkspaceRoot), cancellationToken)
                .ConfigureAwait(false);
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

    /// <summary><c>people/…</c> resolves under <paramref name="entityWorkspaceRoot"/> when set; other paths use <paramref name="projectRoot"/>.</summary>
    private static string ResolveDataFilePath(string projectRoot, string? entityWorkspaceRoot, string relativePath)
    {
        var pr = Path.GetFullPath(projectRoot.Trim());
        var norm = relativePath.Replace('\\', '/').TrimStart('/');
        var baseRoot = norm.StartsWith("people/", StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(entityWorkspaceRoot) ? pr : Path.GetFullPath(entityWorkspaceRoot))
            : pr;
        var full = Path.GetFullPath(Path.Combine(baseRoot, norm.Replace('/', Path.DirectorySeparatorChar)));
        if (!PersonaScenarioScope.IsUnderProjectRoot(pr, full))
            throw new UnauthorizedAccessException("path outside project root");
        return full;
    }
}

public sealed record EntitySearchHit(string EntityKey, string EntityType, string RootPath);
