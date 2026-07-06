namespace AgctorSDK.Core.Rag.Transport;

/// <summary>MCP JSON-RPC over HTTP for backends like Cognee MCP (PRD-025).</summary>
public interface IMcpHttpRagTransport
{
    /// <summary>Invokes an MCP tool via <c>tools/call</c> JSON-RPC.</summary>
    Task<McpToolCallResult> InvokeToolAsync(
        string endpointUrl,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);

    /// <summary>Raw JSON-RPC call (e.g. <c>tools/list</c> for health probes).</summary>
    Task<McpJsonRpcResult> SendAsync(
        string endpointUrl,
        string method,
        object? parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>Parsed MCP tool result text (concatenated content blocks).</summary>
public sealed record McpToolCallResult(
    bool Success,
    string Text,
    string? RawJson = null,
    string? ErrorMessage = null);

/// <summary>Generic MCP JSON-RPC result.</summary>
public sealed record McpJsonRpcResult(
    bool Success,
    string RawJson,
    string? ErrorMessage = null);
