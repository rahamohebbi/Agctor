namespace AgctorSDK.Core.Rag;

/// <summary>User/host settings under Agctor:Rag (persisted in appsettings.User.json in later phases).</summary>
public sealed class RagOptions
{
    /// <summary>Catalog id: None, LightRAG, Cognee.</summary>
    public string DefaultProvider { get; set; } = RagProviderIds.None;

    public LightRagProviderOptions LightRAG { get; set; } = new();

    public CogneeProviderOptions Cognee { get; set; } = new();
}

/// <summary>LightRAG REST sidecar settings (default port 9621).</summary>
public sealed class LightRagProviderOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:9621";

    public string ApiKey { get; set; } = "";

    public RagTransportKind Transport { get; set; } = RagTransportKind.Rest;

    public RagQueryMode DefaultMode { get; set; } = RagQueryMode.Hybrid;
}

/// <summary>Cognee MCP HTTP sidecar settings (default port 8000).</summary>
public sealed class CogneeProviderOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8000";

    public string McpPath { get; set; } = "/mcp";

    public RagTransportKind Transport { get; set; } = RagTransportKind.McpHttp;

    /// <summary>Cognee search tool mode: RAG_COMPLETION, GRAPH_COMPLETION, …</summary>
    public string SearchType { get; set; } = "RAG_COMPLETION";

    /// <summary>LLM key for cognify; may reference env var name in Docker compose.</summary>
    public string LlmApiKey { get; set; } = "";
}
