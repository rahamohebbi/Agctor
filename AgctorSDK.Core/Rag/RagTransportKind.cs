namespace AgctorSDK.Core.Rag;

/// <summary>How an adapter talks to its backend (REST vs MCP over HTTP).</summary>
public enum RagTransportKind
{
    None = 0,
    Rest = 1,
    McpHttp = 2
}
