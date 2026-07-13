# RAG providers (PRD-025)

External retrieval backends for Project Memory **`contextStrategy: rag | graph_rag`**. Operators configure providers from **`/Dashboard/RagProviders`** (same UX patterns as Actor runtime).

**Product spec:** [Project/prd-025/](../../Project/prd-025/prd-025-readme.md)  
**Docker sidecars:** [docker/rag-providers/README.md](../../docker/rag-providers/README.md)

## Architecture delta

Canonical markdown under `.agctor/` stays **source of truth**. RAG sidecars hold **derived indexes** queried at prompt-build time.

```
LlmNode.config.contextStrategy (rag | graph_rag)
        │
        ▼
PersonMemoryMarkdownContextBuilder / PersonMemoryContextTool
        │
        ▼
RagContextService ──► IRagProviderAdapterFactory ──► IRagProviderAdapter
        │                        │
        │                        ├── NullRagProviderAdapter (None)
        │                        ├── LightRagProviderAdapter (REST)
        │                        ├── GraphitiProviderAdapter (REST)
        │                        └── CogneeProviderAdapter (MCP HTTP)
        │
        └── fallback → markdown_focus + user-visible note when provider unavailable
```

| Layer | Location |
| --- | --- |
| Contract + catalog | `AgctorSDK.Core/Rag/` (`IRagProviderAdapter`, `RagProviderCatalog`, `RagOptions`) |
| Transports + adapters | `AgctorSDK.Extensions/Rag/` |
| Appendix orchestration | `AgctorSDK.Core/ProjectMemory/Rag/RagContextService.cs` |
| Docker lifecycle | `AgctorSDK.Host/Services/RagProviderDockerService.cs` |
| Dashboard API + UI | `RagProvidersController`, `/Dashboard/RagProviders` |
| Settings persistence | `IUserRagSettingsService` → `appsettings.User.json` (`Agctor:Rag:*`) |

## Configuration

Merged from `appsettings.json`, `appsettings.User.json`, and environment:

```json
{
  "Agctor": {
    "Rag": {
      "DefaultProvider": "None",
      "LightRAG": {
        "BaseUrl": "http://127.0.0.1:9621",
        "ApiKey": "",
        "DefaultMode": "Hybrid",
        "Transport": "Rest"
      },
      "Graphiti": {
        "BaseUrl": "http://127.0.0.1:8001",
        "ApiKey": "",
        "DefaultGroupId": "agctor",
        "Transport": "Rest"
      },
      "Cognee": {
        "BaseUrl": "http://127.0.0.1:8000",
        "McpPath": "/mcp",
        "SearchType": "RAG_COMPLETION",
        "LlmApiKey": ""
      }
    }
  }
}
```

| Provider id | Docker service | Default port | Transport |
| --- | --- | --- | --- |
| `None` | — | — | Markdown-only fallback |
| `LightRAG` | `lightrag` | 9621 | REST |
| `Graphiti` | `graphiti` (+ `graphiti-db`) | 8001 | REST |
| `Cognee` | `cognee-mcp` | 8000 | MCP HTTP (`/mcp`) |

## REST API (`/api/rag-providers`)

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/rag-providers` | Current provider health snapshot, configured values, catalog |
| GET | `/api/rag-providers/health` | Combined provider + Docker sidecar health |
| PUT | `/api/rag-providers` | Persist selection and provider settings to `appsettings.User.json` |
| POST | `/api/rag-providers/query` | Operator test query (dashboard “Test query” panel) |
| GET | `/api/rag-providers/docker/{providerId}` | Docker sidecar status |
| POST | `/api/rag-providers/docker/{providerId}/install` | Pull image |
| POST | `/api/rag-providers/docker/{providerId}/start` | Start sidecar |
| POST | `/api/rag-providers/docker/{providerId}/stop` | Stop sidecar |

## Terminal commands (shared panel)

Validated `docker compose` presets use context type **`rag-provider`** and compose file **`docker/rag-providers/docker-compose.yml`**.

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/terminal/presets?contextType=rag-provider&contextKey=LightRAG` | Preset commands for a provider |
| POST | `/api/terminal/run` | Run a validated docker compose command (buffered result) |
| POST | `/api/terminal/run/stream` | Same, but SSE-streams stdout/stderr live (pull progress) |

## Project Memory integration

When `contextStrategy` is **`rag`** or **`graph_rag`** on an LlmNode (or `PersonMemoryContextTool`):

1. Resolve active provider from `Agctor:Rag:DefaultProvider` (or optional node override).
2. Query via `RagContextService` using the user message and optional `ragCollectionId` (defaults to scenario id).
3. On success, inject an **External RAG context** appendix into the persona prompt.
4. On failure (`None`, unhealthy sidecar, empty chunks), fall back to **`markdown_focus`** with an explicit note.

Optional LlmNode config keys:

| Key | Purpose |
| --- | --- |
| `contextStrategy` | `markdown_all` \| `markdown_focus` \| `rag` \| `graph_rag` |
| `ragProviderId` | Override catalog id (`LightRAG`, `Graphiti`, `Cognee`, `None`) |
| `ragCollectionId` | Dataset / collection passed to the provider |
| `ragTopK` | Max chunks (default 8) |

Scenarios flow editor shows a read-only hint linking to **`/Dashboard/RagProviders`** when RAG strategies are selected.

## Dashboard pages

| Route | Purpose |
| --- | --- |
| GET `/Dashboard/RagProviders` | Provider catalog, config form, Docker panel, test query |
| GET `/Dashboard/ActorRuntime` | Actor runtime (pattern reference for PRD-012) |

Client script: `wwwroot/js/dashboard/rag-providers-dashboard.js`

## Related PRDs

- **[PRD-013 §15.3](../../Project/prd-013/prd-013-agctor-prd.md)** — semantic retrieval remains optional vs canonical files; v1 delivered via PRD-025 external adapters (not pgvector).
- **PRD-012** — Docker sidecar + terminal panel patterns reused from Actor runtime.
- **PRD-014** — `LlmNode.config.contextStrategy` in scenario flows.

## Tests

| Area | Location |
| --- | --- |
| Core RAG unit tests | `AgctorSDK.Core.Tests/Rag/` |
| Context builder RAG fallback | `AgctorSDK.Core.Tests/ProjectMemory/PersonMemoryMarkdownContextBuilderTests.cs` |
| Host integration (API + page) | `AgctorSDK.Host.IntegrationTests/RagProvider*.cs`, `PersonMemoryRagContextIntegrationTests.cs` |
