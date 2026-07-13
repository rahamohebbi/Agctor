using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.Rag;

/// <summary>
/// Static catalog for the RAG providers dashboard — mirrors <see cref="Runtime.ActorRuntimeCatalog"/> (PRD-025).
/// </summary>
public static class RagProviderCatalog
{
    /// <summary>One row per selectable provider.</summary>
    public static IReadOnlyList<RagProviderDescriptor> All { get; } = new[]
    {
        new RagProviderDescriptor(
            Id: RagProviderIds.None,
            DisplayName: "Markdown only (no RAG)",
            Maturity: "supported",
            Summary: "Uses on-disk Project Memory markdown only. No external retrieval sidecar.",
            Limitations: "No semantic or graph retrieval; loads files via markdown_all / markdown_focus.",
            DeploymentNotes: "Default. No Docker required.",
            Capabilities: new[] { "markdown", "local_files" },
            ContextStrategies: Array.Empty<string>(),
            RequiresDocker: false,
            DefaultTransport: RagTransportKind.None),
        new RagProviderDescriptor(
            Id: RagProviderIds.LightRag,
            DisplayName: "LightRAG",
            Maturity: "supported",
            Summary: "Graph + vector hybrid RAG via LightRAG REST API (Docker sidecar on port 9621).",
            Limitations: "Requires running lightrag compose service and LLM/embedding configuration in provider .env.",
            DeploymentNotes: "docker/rag-providers service lightrag. REST to http://127.0.0.1:9621.",
            Capabilities: new[] { "local_docker", "graph", "hybrid", "vector", "rest" },
            ContextStrategies: new[] { "rag", "graph_rag" },
            RequiresDocker: true,
            DefaultTransport: RagTransportKind.Rest),
        new RagProviderDescriptor(
            Id: RagProviderIds.Graphiti,
            DisplayName: "Graphiti",
            Maturity: "supported",
            Summary: "Temporal knowledge-graph RAG via Graphiti REST API (Docker sidecar on port 8001 + Neo4j).",
            Limitations: "Needs Neo4j companion container and an LLM key (OpenAI or Ollama via OPENAI_BASE_URL in graphiti.env).",
            DeploymentNotes: "docker/rag-providers service graphiti (pulls graphiti-db). REST to http://127.0.0.1:8001.",
            Capabilities: new[] { "local_docker", "graph", "temporal", "agent_memory", "rest" },
            ContextStrategies: new[] { "rag", "graph_rag" },
            RequiresDocker: true,
            DefaultTransport: RagTransportKind.Rest),
        new RagProviderDescriptor(
            Id: RagProviderIds.Cognee,
            DisplayName: "Cognee",
            Maturity: "experimental",
            Summary: "Agent memory graph + RAG completion via Cognee MCP HTTP (Docker sidecar on port 8000).",
            Limitations: "Requires LLM API key for cognify; MCP tool surface may change between Cognee versions.",
            DeploymentNotes: "docker/rag-providers service cognee-mcp. MCP HTTP at /mcp.",
            Capabilities: new[] { "local_docker", "graph", "memory", "mcp" },
            ContextStrategies: new[] { "graph_rag" },
            RequiresDocker: true,
            DefaultTransport: RagTransportKind.McpHttp)
    };

    /// <summary>Case-insensitive lookup by catalog id.</summary>
    public static RagProviderDescriptor? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var canonical = RagProviderIds.Normalize(id);
        return All.FirstOrDefault(d => string.Equals(d.Id, canonical, StringComparison.Ordinal));
    }
}

/// <summary>Dashboard catalog row for one RAG provider.</summary>
public sealed record RagProviderDescriptor(
    string Id,
    string DisplayName,
    string Maturity,
    string Summary,
    string Limitations,
    string DeploymentNotes,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> ContextStrategies,
    bool RequiresDocker,
    RagTransportKind DefaultTransport);
