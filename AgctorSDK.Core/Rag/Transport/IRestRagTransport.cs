using System.Net.Http;

namespace AgctorSDK.Core.Rag.Transport;

/// <summary>HTTP transport for REST-based RAG backends (LightRAG, RAGFlow, …).</summary>
public interface IRestRagTransport
{
    /// <summary>Sends an HTTP request and returns status + body text.</summary>
    Task<RagRestResponse> SendAsync(RagRestCall call, CancellationToken cancellationToken = default);
}

/// <summary>One outbound REST call.</summary>
public sealed record RagRestCall(
    HttpMethod Method,
    string Url,
    string? JsonBody = null,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>Raw REST response for adapter-specific parsing.</summary>
public sealed record RagRestResponse(
    int StatusCode,
    string Body,
    bool IsSuccessStatusCode);
