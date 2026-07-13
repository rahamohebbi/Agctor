using System.Text.Json;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Transport;

namespace AgctorSDK.Extensions.Rag.Providers;

/// <summary>Maps Graphiti REST JSON (/healthcheck, /search, /messages) to Agctor RAG models.</summary>
internal static class GraphitiApiMapper
{
    public static RagHealthResult ParseHealth(RagRestResponse response)
    {
        if (response.StatusCode == 0)
        {
            var detail = string.IsNullOrWhiteSpace(response.Body)
                ? "connection refused or host unreachable"
                : response.Body.Trim();
            return new RagHealthResult(
                RagHealthStatus.Unavailable,
                $"Graphiti sidecar not reachable ({detail}). Start the Docker sidecar or verify BaseUrl.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new RagHealthResult(
                RagHealthStatus.Unavailable,
                $"Graphiti /healthcheck returned HTTP {response.StatusCode}.");
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : "ok";
            var healthy = string.IsNullOrEmpty(status)
                          || status.Equals("healthy", StringComparison.OrdinalIgnoreCase)
                          || status.Equals("ok", StringComparison.OrdinalIgnoreCase);

            return new RagHealthResult(
                healthy ? RagHealthStatus.Healthy : RagHealthStatus.Degraded,
                healthy ? "Graphiti sidecar is reachable." : $"Graphiti status: {status}");
        }
        catch (JsonException)
        {
            return new RagHealthResult(RagHealthStatus.Healthy, "Graphiti /healthcheck returned HTTP 200.");
        }
    }

    public static RagQueryResult ParseSearch(RagRestResponse response)
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
            if (!doc.RootElement.TryGetProperty("facts", out var facts) || facts.ValueKind != JsonValueKind.Array)
                return new RagQueryResult(Array.Empty<RagContextChunk>(), RawDebugJson: response.Body);

            var chunks = new List<RagContextChunk>();
            foreach (var fact in facts.EnumerateArray())
            {
                var text = fact.TryGetProperty("fact", out var f) ? f.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;

                var name = fact.TryGetProperty("name", out var n) ? n.GetString() : null;
                var uuid = fact.TryGetProperty("uuid", out var u) ? u.GetString() : null;
                var label = string.IsNullOrWhiteSpace(name) ? text.Trim() : $"{name}: {text.Trim()}";
                var meta = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(uuid)) meta["uuid"] = uuid;
                if (!string.IsNullOrEmpty(name)) meta["name"] = name;
                chunks.Add(new RagContextChunk(label, SourcePath: null, Metadata: meta));
            }

            return new RagQueryResult(chunks, RawDebugJson: response.Body);
        }
        catch (JsonException)
        {
            return new RagQueryResult(
                Array.Empty<RagContextChunk>(),
                RawDebugJson: response.Body);
        }
    }

    public static RagIngestResult ParseIngest(RagRestResponse response)
    {
        // Graphiti queues episodes asynchronously and returns 202 Accepted.
        if (response.StatusCode is not (200 or 201 or 202) && !response.IsSuccessStatusCode)
        {
            return new RagIngestResult(
                false,
                $"Graphiti ingest failed with HTTP {response.StatusCode}.",
                DocumentId: null);
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            var success = !doc.RootElement.TryGetProperty("success", out var ok) || ok.GetBoolean();
            var message = doc.RootElement.TryGetProperty("message", out var msg)
                ? msg.GetString()
                : null;

            return new RagIngestResult(
                success,
                string.IsNullOrWhiteSpace(message)
                    ? "Text queued for Graphiti episode processing."
                    : message.Trim(),
                DocumentId: null);
        }
        catch (JsonException)
        {
            return new RagIngestResult(true, "Graphiti accepted ingest (non-JSON response).");
        }
    }

    /// <summary>Prefer request CollectionId, then DefaultGroupId — Graphiti scopes memory by group_id.</summary>
    public static string ResolveGroupId(string? collectionId, string? defaultGroupId)
    {
        if (!string.IsNullOrWhiteSpace(collectionId))
            return collectionId.Trim();

        if (!string.IsNullOrWhiteSpace(defaultGroupId))
            return defaultGroupId.Trim();

        return "agctor";
    }

    /// <summary>Stable episode name from Agctor relative path (or a generated id).</summary>
    public static string ToEpisodeName(string? sourcePath)
    {
        var normalized = (sourcePath ?? "").Replace('\\', '/').Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return $"agctor-{Guid.NewGuid():N}";

        return normalized.Replace("/", "__");
    }
}
