using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Rag;

/// <summary>
/// Per-provider dashboard form fields and Docker compose metadata (PRD-025).
/// Mirrors <see cref="Runtime.ActorRuntimeConfigSchema"/>.
/// </summary>
public static class RagProviderConfigSchema
{
    /// <summary>Providers that run a local Docker sidecar via docker compose.</summary>
    public static IReadOnlySet<string> DockerBackedProviders { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RagProviderIds.LightRag,
            RagProviderIds.Graphiti,
            RagProviderIds.Cognee
        };

    /// <summary>Docker Compose service name for a provider id.</summary>
    public static string? GetDockerServiceName(string? providerId)
    {
        return RagProviderIds.Normalize(providerId) switch
        {
            RagProviderIds.LightRag => "lightrag",
            RagProviderIds.Graphiti => "graphiti",
            RagProviderIds.Cognee => "cognee-mcp",
            _ => null
        };
    }

    /// <summary>Dashboard form fields persisted under Agctor:Rag:* keys.</summary>
    public static IReadOnlyList<RagConfigField> GetFields(string? providerId)
    {
        return RagProviderIds.Normalize(providerId) switch
        {
            RagProviderIds.LightRag =>
            [
                new("BaseUrl", "API base URL", "text", "http://127.0.0.1:9621", "http://127.0.0.1:9621",
                    "LightRAG REST API root (Docker sidecar)."),
                new("ApiKey", "API key", "password", "", null,
                    "Optional; set when LightRAG auth is enabled."),
                new("DefaultMode", "Default query mode", "select", "Hybrid", "Hybrid|Vector|Graph|Auto",
                    "Maps to LightRAG query modes."),
                new("Transport", "Transport", "select", "Rest", "Rest",
                    "v1 uses REST only; MCP bridge is optional later.")
            ],
            RagProviderIds.Graphiti =>
            [
                new("BaseUrl", "API base URL", "text", "http://127.0.0.1:8001", "http://127.0.0.1:8001",
                    "Graphiti REST API root (Docker sidecar; Neo4j starts as dependency)."),
                new("ApiKey", "API key", "password", "", null,
                    "Optional gateway key; stock Graphiti REST does not require it."),
                new("DefaultGroupId", "Default group id", "text", "agctor", "agctor",
                    "Graphiti group_id when CollectionId / scenario id is blank."),
                new("Transport", "Transport", "select", "Rest", "Rest",
                    "v1 uses REST (/healthcheck, /search, /messages).")
            ],
            RagProviderIds.Cognee =>
            [
                new("BaseUrl", "MCP base URL", "text", "http://127.0.0.1:8000", "http://127.0.0.1:8000",
                    "Cognee MCP HTTP listener."),
                new("McpPath", "MCP path", "text", "/mcp", "/mcp",
                    "Streamable HTTP or SSE mount path."),
                new("SearchType", "Search type", "select", "CHUNKS",
                    "CHUNKS|RAG_COMPLETION|GRAPH_COMPLETION",
                    "CHUNKS returns indexed text quickly (dashboard test uses CHUNKS). RAG/GRAPH completion runs a full LLM answer and can take several minutes."),
                new("LlmApiKey", "LLM API key", "password", "ollama", "ollama",
                    "For Ollama use the literal value ollama in docker/rag-providers/cognee.env (not a paid key). Dashboard field is optional; Docker reads cognee.env.", Required: false)
            ],
            _ => Array.Empty<RagConfigField>()
        };
    }
}

/// <summary>One configurable field on the RAG providers dashboard.</summary>
public sealed record RagConfigField(
    string Key,
    string Label,
    string FieldType,
    string? DefaultValue,
    string? Placeholder,
    string? HelpText,
    bool Required = false);
