using AgctorSDK.Core.Rag;

namespace AgctorSDK.Host.Services;

/// <summary>Reads effective <see cref="RagOptions"/> from merged configuration (PRD-025).</summary>
public static class RagProviderConfigBuilder
{
    /// <summary>Binds Agctor:Rag from configuration with sane defaults.</summary>
    public static RagOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new RagOptions();
        configuration.GetSection("Agctor:Rag").Bind(options);

        options.DefaultProvider = RagProviderIds.Normalize(options.DefaultProvider);
        if (string.IsNullOrWhiteSpace(options.LightRAG.BaseUrl))
            options.LightRAG.BaseUrl = "http://127.0.0.1:9621";
        if (string.IsNullOrWhiteSpace(options.Cognee.BaseUrl))
            options.Cognee.BaseUrl = "http://127.0.0.1:8000";
        if (string.IsNullOrWhiteSpace(options.Cognee.McpPath))
            options.Cognee.McpPath = "/mcp";
        if (string.IsNullOrWhiteSpace(options.Cognee.SearchType))
            options.Cognee.SearchType = "RAG_COMPLETION";

        return options;
    }
}
