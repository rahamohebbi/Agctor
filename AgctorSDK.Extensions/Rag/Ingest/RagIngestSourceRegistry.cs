using AgctorSDK.Core.Rag.Ingest;

namespace AgctorSDK.Extensions.Rag.Ingest;

/// <summary>DI registry of implemented ingest sources plus static planned sources.</summary>
public sealed class RagIngestSourceRegistry : IRagIngestSourceRegistry
{
    private readonly IReadOnlyDictionary<string, IRagIngestSource> _implemented;

    public RagIngestSourceRegistry(IEnumerable<IRagIngestSource> sources)
    {
        _implemented = sources.ToDictionary(s => s.SourceId, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<RagIngestSourceDescriptor> ListCatalog() => RagIngestSourceCatalog.All;

    /// <inheritdoc />
    public IRagIngestSource? TryGetImplemented(string sourceId)
    {
        var canonical = RagIngestSourceIds.Normalize(sourceId);
        return _implemented.TryGetValue(canonical, out var source) ? source : null;
    }
}
