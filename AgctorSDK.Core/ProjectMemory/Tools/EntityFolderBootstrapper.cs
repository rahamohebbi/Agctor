using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Parsing;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Tools;

/// <summary>
/// When ingest references an unknown <c>entityKey</c>, creates <c>people/&lt;slug&gt;/</c> with
/// <c>entity.yaml</c> plus all required/optional markdown docs from the project type schema so
/// <see cref="Processing.DocumentProjectionService"/> can apply routed intents (e.g. "Melody is 47").
/// </summary>
public static class EntityFolderBootstrapper
{
    /// <summary>
    /// Folder-safe slug: prefers the last path segment so <c>match/people/melody/</c> → <c>melody</c>,
    /// then keeps letters/digits only (lower). Aligns with <see cref="ProjectMemoryPipelineRunner"/> lookup.
    /// </summary>
    public static string SlugFolderSegment(string? rawEntityKey)
    {
        if (string.IsNullOrWhiteSpace(rawEntityKey))
            return "";
        var normalized = rawEntityKey.Trim().Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
            return "";
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Paths from extractors often look like match/people/melody/ — entity folder is the leaf.
        var core = segments.Length > 0 ? segments[^1] : normalized;
        return AlphanumericSlug(core);
    }

    /// <summary>Lowercase slug: letters and digits only (no spaces/punctuation).</summary>
    private static string AlphanumericSlug(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return "";
        var s = segment.Trim().ToLowerInvariant();
        var sb = new StringBuilder(Math.Min(s.Length, 80));
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }

        return sb.Length > 0 ? sb.ToString() : "";
    }

    /// <summary>
    /// Creates starter files if the entity folder is missing or has no metadata file; returns paths written.
    /// </summary>
    public static async Task<IReadOnlyList<string>> TryCreateIfMissingAsync(
        LoadedProjectContext ctx,
        string entityWorkspaceRoot,
        string rawEntityKey,
        IReadOnlyList<RoutedMemoryIntent> intentsForEntity,
        CancellationToken cancellationToken = default)
    {
        var slug = SlugFolderSegment(rawEntityKey);
        if (string.IsNullOrEmpty(slug))
            return Array.Empty<string>();

        var personType = ctx.TypeSchema.EntityTypes.EntityTypes.FirstOrDefault(e =>
                             string.Equals(e.Id, "person", StringComparison.OrdinalIgnoreCase))
                         ?? ctx.TypeSchema.EntityTypes.EntityTypes.FirstOrDefault();
        if (personType == null)
            return Array.Empty<string>();

        var peopleSegment = ExtractBaseFolder(personType.FolderPattern);
        var entityDir = Path.Combine(entityWorkspaceRoot, peopleSegment, slug);
        var metaPath = Path.Combine(entityDir, personType.MetadataFile ?? "entity.yaml");
        if (File.Exists(metaPath))
            return Array.Empty<string>();

        Directory.CreateDirectory(entityDir);

        var displayName = ResolveDisplayName(slug, intentsForEntity);
        var meta = new EntityMetadata
        {
            EntityKey = slug,
            EntityType = personType.Id,
            DisplayName = displayName,
            Aliases = new List<string>(),
            SchemaVersion = 1,
            Status = "active"
        };

        var yaml = ProjectYamlSerializer.Serialize(meta);
        await File.WriteAllTextAsync(metaPath, yaml, cancellationToken).ConfigureAwait(false);
        var written = new List<string> { metaPath };

        var docFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in personType.RequiredDocuments ?? Enumerable.Empty<string>())
            docFiles.Add(d);
        foreach (var d in personType.OptionalDocuments ?? Enumerable.Empty<string>())
            docFiles.Add(d);

        var byFileName = ctx.TypeSchema.DocumentTypes.DocumentTypes
            .Where(d => !string.IsNullOrWhiteSpace(d.FileName))
            .ToDictionary(d => d.FileName.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in docFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byFileName.TryGetValue(fileName, out var docType))
                continue;

            var path = Path.Combine(entityDir, fileName);
            if (File.Exists(path))
                continue;

            var md = BuildStarterMarkdown(displayName, fileName, docType);
            await File.WriteAllTextAsync(path, md, cancellationToken).ConfigureAwait(false);
            written.Add(path);
        }

        return written;
    }

    private static string ResolveDisplayName(string slug, IReadOnlyList<RoutedMemoryIntent> intentsForEntity)
    {
        foreach (var i in intentsForEntity)
        {
            var a = i.Original.Attribute ?? "";
            if (string.Equals(i.Original.KnowledgeType, "person", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a, "name", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(i.Original.Value))
                return i.Original.Value.Trim();

            if (string.Equals(i.Original.KnowledgeType, "profile_fact", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a, "name", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(i.Original.Value))
                return i.Original.Value.Trim();
        }

        if (slug.Length == 0)
            return slug;
        return char.ToUpperInvariant(slug[0]) + slug.Substring(1);
    }

    private static string BuildStarterMarkdown(string displayName, string fileName, DocumentTypeDef docType)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var titleSuffix = stem.Length > 0
            ? char.ToUpperInvariant(stem[0]) + stem.Substring(1)
            : "Document";
        var titleLine = $"# {displayName} {titleSuffix}";
        var pairs = docType.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => (SectionTitle: s.Trim(), Body: ""))
            .ToList();
        return DocumentParser.Compose(titleLine, pairs);
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
