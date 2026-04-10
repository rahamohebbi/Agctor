using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Loading;

/// <summary>
/// Discovers entity folders (e.g. <c>people/&lt;key&gt;/</c>) and validates required docs.
/// </summary>
public sealed class EntityRegistry : IEntityRegistry
{
    public Task<IReadOnlyList<EntityRecord>> DiscoverAsync(LoadedProjectContext ctx, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = new List<EntityRecord>();
        var root = ctx.ProjectRoot;
        var entityTypes = ctx.TypeSchema.EntityTypes.EntityTypes;

        foreach (var et in entityTypes)
        {
            // Pattern: people/{entityKey}/
            var baseSegment = ExtractBaseFolder(et.FolderPattern);
            var basePath = Path.Combine(root, baseSegment);
            if (!Directory.Exists(basePath))
                continue;

            foreach (var dir in Directory.EnumerateDirectories(basePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entityKey = Path.GetFileName(dir);
                var metaPath = Path.Combine(dir, et.MetadataFile);
                if (!File.Exists(metaPath))
                    continue;

                var meta = ProjectYamlSerializer.DeserializeFromFile<EntityMetadata>(metaPath);
                if (!string.Equals(meta.EntityKey, entityKey, StringComparison.OrdinalIgnoreCase))
                {
                    // still allow — warn elsewhere
                }

                var docs = new List<string>();
                foreach (var req in et.RequiredDocuments)
                {
                    var dp = Path.Combine(dir, req);
                    if (File.Exists(dp))
                        docs.Add(req);
                }

                foreach (var opt in et.OptionalDocuments ?? Enumerable.Empty<string>())
                {
                    var dp = Path.Combine(dir, opt);
                    if (File.Exists(dp))
                        docs.Add(opt);
                }

                list.Add(new EntityRecord
                {
                    EntityKey = entityKey,
                    EntityType = et.Id,
                    RootPath = dir,
                    Metadata = meta,
                    DocumentRelativePaths = docs
                });
            }
        }

        return Task.FromResult<IReadOnlyList<EntityRecord>>(list);
    }

    /// <summary>From <c>people/{entityKey}/</c> return <c>people</c>.</summary>
    private static string ExtractBaseFolder(string folderPattern)
    {
        var s = folderPattern.Trim().Replace('/', Path.DirectorySeparatorChar);
        var idx = s.IndexOf("{entityKey}", StringComparison.OrdinalIgnoreCase);
        if (idx <= 0)
            return s.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar)[0];
        var prefix = s.Substring(0, idx).TrimEnd(Path.DirectorySeparatorChar);
        return prefix;
    }
}
