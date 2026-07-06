using System.Text;

namespace AgctorSDK.Core.Rag.Ingest;

/// <summary>
/// Fans out documents from an <see cref="IRagIngestSource"/> into the selected RAG provider adapter.
/// </summary>
public sealed class RagIngestOrchestrator
{
    private readonly IRagIngestSourceRegistry _sources;
    private readonly IRagProviderAdapterFactory _factory;

    public RagIngestOrchestrator(IRagIngestSourceRegistry sources, IRagProviderAdapterFactory factory)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc cref="IRagIngestSource.PreviewAsync"/>
    public Task<RagIngestSourcePreview> PreviewAsync(
        string sourceId,
        RagIngestSourceContext context,
        CancellationToken cancellationToken = default)
    {
        var source = ResolveSource(sourceId);
        return source.PreviewAsync(context, cancellationToken);
    }

    /// <summary>Enumerate source documents and ingest each via the provider adapter.</summary>
    public async Task<RagIngestBatchResult> IngestAsync(
        string providerId,
        string sourceId,
        RagIngestSourceContext context,
        CancellationToken cancellationToken = default)
    {
        var canonicalProvider = RagProviderIds.Normalize(providerId);
        if (string.Equals(canonicalProvider, RagProviderIds.None, StringComparison.Ordinal))
        {
            return new RagIngestBatchResult(
                false, canonicalProvider, RagIngestSourceIds.Normalize(sourceId),
                0, 0, 0, Array.Empty<RagIngestItemResult>(),
                "Select LightRAG or Cognee — Markdown only does not use external ingest.");
        }

        var source = ResolveSource(sourceId);
        IRagProviderAdapter adapter;
        try
        {
            adapter = _factory.CreateProvider(canonicalProvider);
        }
        catch (Exception ex)
        {
            return new RagIngestBatchResult(
                false, canonicalProvider, source.SourceId,
                0, 0, 0, Array.Empty<RagIngestItemResult>(),
                ex.Message);
        }

        var health = await adapter.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (health.Status is RagHealthStatus.Unavailable or RagHealthStatus.NotConfigured)
        {
            return new RagIngestBatchResult(
                false, canonicalProvider, source.SourceId,
                0, 0, 0, Array.Empty<RagIngestItemResult>(),
                health.Message);
        }

        // Cognee remember runs a full LLM cognify pipeline per call — batch by dataset to avoid 80+ minute hangs.
        if (string.Equals(canonicalProvider, RagProviderIds.Cognee, StringComparison.Ordinal))
            return await IngestCogneeBatchedAsync(adapter, source, context, cancellationToken).ConfigureAwait(false);

        var items = new List<RagIngestItemResult>();
        var succeeded = 0;
        var failed = 0;

        await foreach (var doc in source.EnumerateDocumentsAsync(context, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var collectionId = doc.CollectionId ?? context.CollectionId;
                var result = await adapter.IngestAsync(
                    new RagIngestRequest(doc.RelativePath, collectionId, doc.Content, doc.Metadata),
                    cancellationToken).ConfigureAwait(false);

                items.Add(new RagIngestItemResult(doc.RelativePath, result.Success, result.Message, result.DocumentId));
                if (result.Success) succeeded++;
                else failed++;
            }
            catch (Exception ex)
            {
                items.Add(new RagIngestItemResult(doc.RelativePath, false, ex.Message));
                failed++;
            }
        }

        var total = items.Count;
        var ok = total > 0 && failed == 0;
        var message = total == 0
            ? "No documents matched this source — check project root and paths."
            : ok
                ? $"Ingested {succeeded} document(s) into {canonicalProvider}."
                : $"Ingested {succeeded}/{total} document(s); {failed} failed.";

        return new RagIngestBatchResult(
            ok, canonicalProvider, source.SourceId,
            total, succeeded, failed, items, message);
    }

    /// <summary>
    /// Groups markdown by dataset and sends one Cognee remember call per group.
    /// Each remember still cognifies, but batching avoids one slow MCP round-trip per file.
    /// </summary>
    private static async Task<RagIngestBatchResult> IngestCogneeBatchedAsync(
        IRagProviderAdapter adapter,
        IRagIngestSource source,
        RagIngestSourceContext context,
        CancellationToken cancellationToken)
    {
        var docs = new List<RagIngestDocument>();
        await foreach (var doc in source.EnumerateDocumentsAsync(context, cancellationToken).ConfigureAwait(false))
            docs.Add(doc);

        if (docs.Count == 0)
        {
            return new RagIngestBatchResult(
                false, RagProviderIds.Cognee, source.SourceId,
                0, 0, 0, Array.Empty<RagIngestItemResult>(),
                "No documents matched this source — check project root and paths.");
        }

        var groups = docs.GroupBy(d => ResolveDatasetName(d, context));
        var items = new List<RagIngestItemResult>();
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        IReadOnlySet<string>? existing = null;
        if (!ShouldForceReingest(context) && adapter is IRagCollectionCatalog catalog)
        {
            try
            {
                existing = await catalog.ListCollectionIdsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Listing is best-effort; proceed with full ingest when Cognee is mid-startup.
            }
        }

        var batchIndex = 0;
        var batchTotal = groups.Count();

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batchIndex++;
            var dataset = group.Key;
            var paths = group.Select(d => d.RelativePath).ToList();

            if (existing?.Contains(dataset) == true)
            {
                foreach (var path in paths)
                {
                    items.Add(new RagIngestItemResult(
                        path,
                        true,
                        $"Skipped dataset '{dataset}' — already indexed in Cognee (enable Force re-ingest to refresh)."));
                    succeeded++;
                    skipped++;
                }

                continue;
            }

            var combined = CombineDocumentsForCognee(group);

            try
            {
                var result = await adapter.IngestAsync(
                    new RagIngestRequest(
                        SourcePath: $"{dataset}-batch.md",
                        CollectionId: dataset,
                        Content: combined),
                    cancellationToken).ConfigureAwait(false);

                var batchNote = result.Success
                    ? $" (dataset {batchIndex}/{batchTotal}: {dataset})"
                    : "";
                foreach (var path in paths)
                {
                    items.Add(new RagIngestItemResult(
                        path,
                        result.Success,
                        (result.Message ?? "") + batchNote,
                        result.DocumentId));
                    if (result.Success) succeeded++;
                    else failed++;
                }
            }
            catch (Exception ex)
            {
                foreach (var path in paths)
                {
                    items.Add(new RagIngestItemResult(path, false, ex.Message));
                    failed++;
                }
            }
        }

        var total = items.Count;
        var ok = total > 0 && failed == 0;
        var batchCount = groups.Count();
        var message = total == 0
            ? "No documents matched this source — check project root and paths."
            : skipped > 0 && skipped == total
                ? $"All {batchCount} dataset(s) already indexed in Cognee — enable Force re-ingest to refresh."
                : ok
                    ? skipped > 0
                        ? $"Ingested {succeeded - skipped} new document(s), skipped {skipped} already-indexed file(s) across {batchCount} dataset batch(es)."
                        : $"Ingested {succeeded} document(s) into Cognee across {batchCount} dataset batch(es)."
                    : $"Ingested {succeeded}/{total} document(s) into Cognee; {failed} failed.";

        return new RagIngestBatchResult(
            ok, RagProviderIds.Cognee, source.SourceId,
            total, succeeded, failed, items, message);
    }

    private static string ResolveDatasetName(RagIngestDocument doc, RagIngestSourceContext context) =>
        doc.CollectionId
        ?? context.CollectionId
        ?? "agctor";

    private static bool ShouldForceReingest(RagIngestSourceContext context) =>
        context.Options != null
        && context.Options.TryGetValue(RagIngestOptionKeys.ForceReingest, out var value)
        && bool.TryParse(value, out var force)
        && force;

    private static string CombineDocumentsForCognee(IEnumerable<RagIngestDocument> docs)
    {
        var sb = new StringBuilder();
        foreach (var doc in docs)
        {
            sb.Append("--- ").Append(doc.RelativePath).AppendLine(" ---");
            sb.AppendLine(doc.Content);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private IRagIngestSource ResolveSource(string sourceId)
    {
        var canonical = RagIngestSourceIds.Normalize(sourceId);
        var source = _sources.TryGetImplemented(canonical);
        if (source == null)
        {
            var descriptor = RagIngestSourceCatalog.All.FirstOrDefault(d =>
                string.Equals(d.Id, canonical, StringComparison.OrdinalIgnoreCase));
            var label = descriptor?.DisplayName ?? canonical;
            throw new InvalidOperationException($"Ingest source '{label}' is not implemented yet.");
        }

        return source;
    }
}
