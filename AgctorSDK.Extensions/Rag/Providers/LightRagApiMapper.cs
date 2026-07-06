using System.Text.Json;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Transport;

namespace AgctorSDK.Extensions.Rag.Providers;

/// <summary>Maps LightRAG REST JSON to Agctor <see cref="RagQueryResult"/> / health models.</summary>
internal static class LightRagApiMapper
{
    /// <summary>Agctor query mode → LightRAG <c>/query/data</c> mode string.</summary>
    public static string ToLightRagMode(RagQueryMode mode, RagQueryMode defaultMode) =>
        mode switch
        {
            RagQueryMode.Vector => "naive",
            RagQueryMode.Graph => "local",
            RagQueryMode.Hybrid => "hybrid",
            RagQueryMode.Auto => defaultMode switch
            {
                RagQueryMode.Vector => "naive",
                RagQueryMode.Graph => "local",
                RagQueryMode.Hybrid => "hybrid",
                _ => "mix"
            },
            _ => "mix"
        };

    public static RagHealthResult ParseHealth(RagRestResponse response)
    {
        if (response.StatusCode == 0)
        {
            var detail = string.IsNullOrWhiteSpace(response.Body)
                ? "connection refused or host unreachable"
                : response.Body.Trim();
            return new RagHealthResult(
                RagHealthStatus.Unavailable,
                $"LightRAG sidecar not reachable ({detail}). Start the Docker sidecar or verify BaseUrl.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new RagHealthResult(
                RagHealthStatus.Unavailable,
                $"LightRAG /health returned HTTP {response.StatusCode}.");
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : "ok";
            var version = doc.RootElement.TryGetProperty("api_version", out var v) ? v.GetString() : null;
            var healthy = string.IsNullOrEmpty(status)
                          || status.Equals("healthy", StringComparison.OrdinalIgnoreCase)
                          || status.Equals("ok", StringComparison.OrdinalIgnoreCase);

            return new RagHealthResult(
                healthy ? RagHealthStatus.Healthy : RagHealthStatus.Degraded,
                healthy ? "LightRAG sidecar is reachable." : $"LightRAG status: {status}",
                version);
        }
        catch (JsonException)
        {
            return new RagHealthResult(RagHealthStatus.Healthy, "LightRAG /health returned HTTP 200.");
        }
    }

    public static RagQueryResult ParseQueryData(RagRestResponse response)
    {
        if (!response.IsSuccessStatusCode)
        {
            return new RagQueryResult(
                Array.Empty<RagContextChunk>(),
                RawDebugJson: response.Body);
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return new RagQueryResult(Array.Empty<RagContextChunk>(), RawDebugJson: response.Body);

            var chunks = new List<RagContextChunk>();
            if (data.TryGetProperty("chunks", out var chunkArr) && chunkArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in chunkArr.EnumerateArray())
                {
                    var text = c.TryGetProperty("content", out var content) ? content.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var path = c.TryGetProperty("file_path", out var fp) ? fp.GetString() : null;
                    var refId = c.TryGetProperty("reference_id", out var rid) ? rid.GetString() : null;
                    var meta = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (!string.IsNullOrEmpty(refId)) meta["reference_id"] = refId;
                    chunks.Add(new RagContextChunk(text.Trim(), SourcePath: path, Metadata: meta));
                }
            }

            // Fallback: entity descriptions when no chunks returned.
            if (chunks.Count == 0 && data.TryGetProperty("entities", out var entities) &&
                entities.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in entities.EnumerateArray())
                {
                    var name = e.TryGetProperty("entity_name", out var n) ? n.GetString() : null;
                    var desc = e.TryGetProperty("description", out var d) ? d.GetString() : null;
                    if (string.IsNullOrWhiteSpace(desc)) continue;
                    var path = e.TryGetProperty("file_path", out var fp) ? fp.GetString() : null;
                    var label = string.IsNullOrWhiteSpace(name) ? desc : $"{name}: {desc}";
                    chunks.Add(new RagContextChunk(label.Trim(), SourcePath: path));
                }
            }

            return new RagQueryResult(chunks, RawDebugJson: response.Body);
        }
        catch (JsonException ex)
        {
            return new RagQueryResult(
                Array.Empty<RagContextChunk>(),
                RawDebugJson: response.Body);
        }
    }

    public static RagIngestResult ParseIngest(RagRestResponse response)
    {
        if (response.StatusCode == 409)
        {
            if (response.Body.Contains("already contains", StringComparison.OrdinalIgnoreCase))
            {
                return new RagIngestResult(
                    true,
                    "Already indexed in LightRAG (duplicate skipped).",
                    DocumentId: null);
            }

            return new RagIngestResult(
                false,
                $"LightRAG ingest conflict (HTTP 409): {Truncate(response.Body, 240)}",
                DocumentId: null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new RagIngestResult(
                false,
                $"LightRAG ingest failed with HTTP {response.StatusCode}.",
                DocumentId: null);
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : "success";
            var docId = doc.RootElement.TryGetProperty("document_id", out var id) ? id.GetString() : null;
            var ok = status != null && (
                status.Contains("success", StringComparison.OrdinalIgnoreCase)
                || status.Contains("queued", StringComparison.OrdinalIgnoreCase)
                || status.Contains("pending", StringComparison.OrdinalIgnoreCase));

            return new RagIngestResult(ok, ok ? "Text queued for LightRAG indexing." : status ?? "unknown", docId);
        }
        catch (JsonException)
        {
            return new RagIngestResult(true, "LightRAG accepted ingest (non-JSON response).");
        }
    }

    /// <summary>
    /// LightRAG deduplicates by <c>file_source</c> basename only — flatten the Agctor relative path so
    /// many <c>profile.md</c> files under different people folders do not collide.
    /// </summary>
    public static string ToUniqueFileSource(string relativePath)
    {
        var normalized = (relativePath ?? "").Replace('\\', '/').Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return $"agctor-{Guid.NewGuid():N}.md";

        var flat = normalized.Replace("/", "__");
        return flat.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? flat : flat + ".md";
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s[..max];
}
