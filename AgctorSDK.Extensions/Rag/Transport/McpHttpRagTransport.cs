using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgctorSDK.Core.Rag.Transport;

namespace AgctorSDK.Extensions.Rag.Transport;

/// <summary>
/// MCP Streamable HTTP client for Cognee: initialize session, send JSON-RPC, parse SSE or JSON bodies.
/// </summary>
public sealed class McpHttpRagTransport : IMcpHttpRagTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, string> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId;

    public McpHttpRagTransport(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public Task<McpToolCallResult> InvokeToolAsync(
        string endpointUrl,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default) =>
        InvokeToolInternalAsync(endpointUrl, toolName, arguments, cancellationToken);

    /// <inheritdoc />
    public async Task<McpJsonRpcResult> SendAsync(
        string endpointUrl,
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
            return new McpJsonRpcResult(false, "", "MCP endpoint URL is required.");

        var rpc = await PostJsonRpcAsync(endpointUrl, method, parameters, cancellationToken).ConfigureAwait(false);
        if (!rpc.Success)
            return new McpJsonRpcResult(false, rpc.Body, rpc.ErrorMessage);

        return new McpJsonRpcResult(true, rpc.Body);
    }

    private async Task<McpToolCallResult> InvokeToolInternalAsync(
        string endpointUrl,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
            return new McpToolCallResult(false, "", ErrorMessage: "MCP endpoint URL is required.");

        var rpc = await PostJsonRpcAsync(
            endpointUrl,
            "tools/call",
            new Dictionary<string, object?>
            {
                ["name"] = toolName,
                ["arguments"] = arguments
            },
            cancellationToken).ConfigureAwait(false);

        if (!rpc.Success)
            return new McpToolCallResult(false, "", rpc.Body, rpc.ErrorMessage);

        return ParseToolResponse(rpc.Body);
    }

    private async Task<(bool Success, string Body, string? ErrorMessage)> PostJsonRpcAsync(
        string endpointUrl,
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await EnsureSessionAsync(endpointUrl, cancellationToken).ConfigureAwait(false);

        var result = await PostJsonRpcOnceAsync(endpointUrl, method, parameters, cancellationToken).ConfigureAwait(false);
        if (result.Success || result.ErrorMessage == null
            || !result.ErrorMessage.Contains("session", StringComparison.OrdinalIgnoreCase))
        {
            return (result.Success, result.Body, result.ErrorMessage);
        }

        // Session expired — re-initialize once and retry.
        _sessions.TryRemove(endpointUrl, out _);
        await EnsureSessionAsync(endpointUrl, cancellationToken).ConfigureAwait(false);
        var retry = await PostJsonRpcOnceAsync(endpointUrl, method, parameters, cancellationToken).ConfigureAwait(false);
        return (retry.Success, retry.Body, retry.ErrorMessage);
    }

    private async Task EnsureSessionAsync(string endpointUrl, CancellationToken cancellationToken)
    {
        if (_sessions.ContainsKey(endpointUrl))
            return;

        var gate = _sessionLocks.GetOrAdd(endpointUrl, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessions.ContainsKey(endpointUrl))
                return;

            var init = await PostJsonRpcOnceAsync(
                endpointUrl,
                "initialize",
                new Dictionary<string, object?>
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new Dictionary<string, object?>(),
                    ["clientInfo"] = new Dictionary<string, object?>
                    {
                        ["name"] = "agctor",
                        ["version"] = "1.0"
                    }
                },
                cancellationToken,
                requireSession: false).ConfigureAwait(false);

            if (!init.Success)
                return;

            if (!string.IsNullOrWhiteSpace(init.SessionId))
                _sessions[endpointUrl] = init.SessionId!;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(bool Success, string Body, string? ErrorMessage, string? SessionId)> PostJsonRpcOnceAsync(
        string endpointUrl,
        string method,
        object? parameters,
        CancellationToken cancellationToken,
        bool requireSession = true)
    {
        var id = Interlocked.Increment(ref _nextId);
        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new Dictionary<string, object?>()
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (requireSession && _sessions.TryGetValue(endpointUrl, out var sessionId) && !string.IsNullOrWhiteSpace(sessionId))
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);

        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return (false, "", $"MCP endpoint not reachable: {ex.Message}", null);
        }
        catch (OperationCanceledException ex)
        {
            return (false, "", ex.Message ?? "MCP request timed out.", null);
        }

        using (response)
        {
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var responseSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var ids)
                ? ids.FirstOrDefault()
                : null;

            if (!string.IsNullOrWhiteSpace(responseSessionId))
                _sessions[endpointUrl] = responseSessionId!;

            var body = ExtractJsonPayload(rawBody);

            if (!response.IsSuccessStatusCode)
            {
                return (false, body, $"MCP HTTP {(int)response.StatusCode}: {Truncate(body, 400)}", responseSessionId);
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    var msg = err.TryGetProperty("message", out var m) ? m.GetString() : err.GetRawText();
                    return (false, body, msg ?? "MCP error", responseSessionId);
                }
            }
            catch (JsonException ex)
            {
                return (false, body, $"Invalid MCP JSON: {ex.Message}", responseSessionId);
            }

            return (true, body, null, responseSessionId);
        }
    }

    /// <summary>Parse Streamable HTTP SSE bodies or plain JSON-RPC JSON.</summary>
    internal static string ExtractJsonPayload(string rawBody)
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

    internal static McpToolCallResult ParseToolResponse(string body)
    {
        body = ExtractJsonPayload(body);
        if (string.IsNullOrWhiteSpace(body))
            return new McpToolCallResult(false, "", body, "Empty MCP response.");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : err.GetRawText();
                return new McpToolCallResult(false, "", body, msg ?? "MCP error");
            }

            if (!root.TryGetProperty("result", out var result))
                return new McpToolCallResult(false, "", body, "MCP response missing result.");

            if (result.TryGetProperty("isError", out var isErr) &&
                (isErr.ValueKind == JsonValueKind.True ||
                 (isErr.ValueKind == JsonValueKind.String && isErr.GetString() == "true")))
            {
                var errText = ExtractContentText(result);
                return new McpToolCallResult(false, errText, body, errText);
            }

            var text = ExtractContentText(result);
            return new McpToolCallResult(true, text, body);
        }
        catch (JsonException ex)
        {
            return new McpToolCallResult(false, "", body, $"Invalid MCP JSON: {ex.Message}");
        }
    }

    private static string ExtractContentText(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return result.GetRawText();

        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("text", out var textEl))
            {
                var t = textEl.GetString();
                if (!string.IsNullOrEmpty(t))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(t);
                }
            }
        }

        return sb.Length > 0 ? sb.ToString() : result.GetRawText();
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s[..max];
}
