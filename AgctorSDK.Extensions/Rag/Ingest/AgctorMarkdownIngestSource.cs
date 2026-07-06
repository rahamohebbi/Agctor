namespace AgctorSDK.Extensions.Rag.Ingest;

using AgctorSDK.Core.Rag.Ingest;

/// <summary>
/// Reads canonical Project Memory markdown from disk for RAG sidecar ingest.
/// Covers `.agctor/**/*.md` (excluding noisy log folders) and scenario entity markdown.
/// </summary>
public sealed class AgctorMarkdownIngestSource : IRagIngestSource
{
    /// <summary>Skip rebuild logs and very large files.</summary>
    private const long MaxFileBytes = 512_000;

    private static readonly string[] ExcludedPathSegments = ["/logs/", "\\logs\\", "/.git/", "\\.git\\"];

    /// <inheritdoc />
    public string SourceId => RagIngestSourceIds.AgctorMarkdown;

    /// <inheritdoc />
    public Task<RagIngestSourcePreview> PreviewAsync(
        RagIngestSourceContext context,
        CancellationToken cancellationToken = default)
    {
        var (root, error) = ResolveProjectRoot(context.ProjectRoot);
        if (error != null)
        {
            return Task.FromResult(new RagIngestSourcePreview(
                0, Array.Empty<string>(), error));
        }

        var paths = CollectMarkdownPaths(root!, cancellationToken).ToList();
        var sample = paths.Take(8).Select(p => Path.GetRelativePath(root!, p).Replace('\\', '/')).ToList();
        var batchCount = paths
            .Select(p => ResolveCollectionId(Path.GetRelativePath(root!, p).Replace('\\', '/'), context.CollectionId))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var message = paths.Count == 0
            ? "No markdown files found under `.agctor/` or `scenarios/*/people/`."
            : $"Found {paths.Count} markdown file(s) in {batchCount} dataset batch(es) ready to ingest.";

        return Task.FromResult(new RagIngestSourcePreview(paths.Count, sample, message, batchCount));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RagIngestDocument> EnumerateDocumentsAsync(
        RagIngestSourceContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (root, error) = ResolveProjectRoot(context.ProjectRoot);
        if (error != null)
            yield break;

        foreach (var fullPath in CollectMarkdownPaths(root!, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content;
            try
            {
                var info = new FileInfo(fullPath);
                if (info.Length > MaxFileBytes)
                    continue;

                content = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
                continue;

            var relative = Path.GetRelativePath(root!, fullPath).Replace('\\', '/');
            var collectionId = ResolveCollectionId(relative, context.CollectionId);

            yield return new RagIngestDocument(relative, content.Trim(), collectionId);
        }
    }

    internal static (string? Root, string? Error) ResolveProjectRoot(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return (null, "Project root is not configured. Set Agctor:ProjectMemory:ProjectRoot on the Maintenance page.");

        var root = Path.GetFullPath(projectRoot.Trim());
        if (!Directory.Exists(root))
            return (null, $"Project root does not exist: {root}");

        if (!Directory.Exists(Path.Combine(root, ".agctor")))
            return (null, "Project root must contain a `.agctor` directory.");

        return (root, null);
    }

    internal static string? ResolveCollectionId(string relativePath, string? overrideCollectionId)
    {
        if (!string.IsNullOrWhiteSpace(overrideCollectionId))
            return overrideCollectionId.Trim();

        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2
            && parts[0].Equals("scenarios", StringComparison.OrdinalIgnoreCase))
            return parts[1];

        return "agctor";
    }

    private static IEnumerable<string> CollectMarkdownPaths(string projectRoot, CancellationToken cancellationToken)
    {
        foreach (var path in EnumerateAgctorMarkdown(projectRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return path;
        }

        foreach (var path in EnumerateScenarioPeopleMarkdown(projectRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return path;
        }
    }

    private static IEnumerable<string> EnumerateAgctorMarkdown(string projectRoot)
    {
        var agctorDir = Path.Combine(projectRoot, ".agctor");
        if (!Directory.Exists(agctorDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(agctorDir, "*.md", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(file))
                continue;

            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateScenarioPeopleMarkdown(string projectRoot)
    {
        var scenariosDir = Path.Combine(projectRoot, "scenarios");
        if (!Directory.Exists(scenariosDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(scenariosDir, "*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
            if (!rel.Contains("/people/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (ShouldSkipPath(file))
                continue;

            yield return file;
        }
    }

    private static bool ShouldSkipPath(string fullPath)
    {
        foreach (var segment in ExcludedPathSegments)
        {
            if (fullPath.Contains(segment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
