using System.Text.Json;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Rag.Transport;

namespace AgctorSDK.Extensions.Rag.Providers;

/// <summary>Maps Cognee MCP tool responses to Agctor RAG models.</summary>
internal static class CogneeMcpMapper
{
    /// <summary>Pick Cognee search_type from Agctor query mode.</summary>
    public static string ResolveSearchType(RagQueryMode mode, string configuredDefault)
    {
        var configured = string.IsNullOrWhiteSpace(configuredDefault)
            ? "RAG_COMPLETION"
            : configuredDefault.Trim().ToUpperInvariant();

        return mode switch
        {
            RagQueryMode.Graph => "GRAPH_COMPLETION",
            RagQueryMode.Vector => "CHUNKS",
            RagQueryMode.Hybrid => configured is "GRAPH_COMPLETION" or "RAG_COMPLETION" ? configured : "RAG_COMPLETION",
            _ => configured is "GRAPH_COMPLETION" or "RAG_COMPLETION" or "CHUNKS" ? configured : "RAG_COMPLETION"
        };
    }

    public static RagQueryResult ParseSearch(McpToolCallResult mcp, RagQueryMode mode)
    {
        if (!mcp.Success)
        {
            return new RagQueryResult(
                Array.Empty<RagContextChunk>(),
                RawDebugJson: mcp.RawJson);
        }

        var text = mcp.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
            return new RagQueryResult(Array.Empty<RagContextChunk>(), RawDebugJson: mcp.RawJson);

        // CHUNKS may return JSON array; wrap plain text or LLM answers as single/multiple chunks.
        if (text.StartsWith('[') || text.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var chunks = new List<RagContextChunk>();
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var chunkText = item.ValueKind == JsonValueKind.String
                            ? item.GetString()
                            : item.TryGetProperty("text", out var t) ? t.GetString()
                            : item.TryGetProperty("content", out var c) ? c.GetString()
                            : item.GetRawText();
                        if (!string.IsNullOrWhiteSpace(chunkText))
                            chunks.Add(new RagContextChunk(chunkText.Trim()));
                    }

                    if (chunks.Count > 0)
                        return new RagQueryResult(chunks, RawDebugJson: mcp.RawJson);
                }
            }
            catch (JsonException)
            {
                // fall through — treat as plain text
            }
        }

        return new RagQueryResult(
            new[] { new RagContextChunk(text, Metadata: new Dictionary<string, string> { ["search_mode"] = mode.ToString() }) },
            RawDebugJson: mcp.RawJson);
    }

    public static RagIngestResult ParseCognify(McpToolCallResult mcp)
    {
        if (!mcp.Success)
            return new RagIngestResult(false, mcp.ErrorMessage ?? "Cognee cognify failed.");

        var msg = string.IsNullOrWhiteSpace(mcp.Text) ? "Cognee cognify accepted." : mcp.Text.Trim();
        return new RagIngestResult(true, msg);
    }

    /// <summary>Parse <c>list_datasets_json</c> structuredContent.datasets[].name values.</summary>
    public static IReadOnlySet<string> ParseDatasetNames(McpToolCallResult mcp)
    {
        if (!mcp.Success || string.IsNullOrWhiteSpace(mcp.RawJson))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var payload = ExtractJsonRpcResultPayload(mcp.RawJson);
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("result", out var result))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!result.TryGetProperty("structuredContent", out var structured)
                || !structured.TryGetProperty("datasets", out var datasets)
                || datasets.ValueKind != JsonValueKind.Array)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in datasets.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var nameEl))
                {
                    var name = nameEl.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name.Trim());
                }
            }

            return names;
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string ExtractJsonRpcResultPayload(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return rawBody;

        var trimmed = rawBody.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return trimmed;

        var lastData = "";
        foreach (var line in rawBody.Split('\n'))
        {
            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                lastData = line["data:".Length..].Trim();
        }

        return string.IsNullOrWhiteSpace(lastData) ? rawBody.Trim() : lastData;
    }
}
