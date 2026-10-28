# RAG providers (PRD-025)

External retrieval backends for Project Memory **`contextStrategy: rag | graph_rag`**. Operators configure providers from **`/Dashboard/RagProviders`** (same UX patterns as Actor runtime).

**Product spec:** [Project/prd-025/](../../Project/prd-025/prd-025-readme.md)  
**Docker sidecars:** [docker/rag-providers/README.md](../../docker/rag-providers/README.md)

## Provider kinds

The dashboard shares one catalog with explicit kinds:

| Kind | Purpose | Action |
|---|---|---|
| Knowledge RAG | Retrieve relevant document or graph context using LightRAG, Graphiti or Cognee | Configure, index and test retrieval |
| Scene Memory | Retrieve manually recorded entity state and change history | Open the Scene Memory workspace and test directly or through an agent |
| File memory | Read Project Memory markdown directly | Use Markdown only |

`GET /api/rag-providers` includes `kind` (`KnowledgeRag`, `SceneMemory`, `Markdown`) and
`canBeDefaultProvider` on catalog entries. Scene Memory works alongside the selected RAG provider;
it uses `ISceneMemoryStore` and `SceneMemoryQueryTool`, and cannot be selected as the default
`IRagProviderAdapter`. Its current MVP supports manual facts and exact-name queries without a model
or Docker. Open `/Dashboard/RagProviders?provider=SceneMemory` to find its workspace.

## Shared retrieval contract

Both provider families implement `IRetrievalProvider<TRequest, TResult>` (Core/Retrieval):
`ProviderId`, `GetHealthAsync`, and `QueryAsync`. The existing `RagHealthResult` and `RagHealthStatus`
types are reused for compatibility. RAG retains `RagQueryRequest`/`RagQueryResult`; Scene Memory uses
`SceneMemoryQueryRequest`/`SceneQueryResult`, preserving workspace, entity, sequence and provenance.

`IRagProviderAdapter` adds document ingestion to the shared read contract. Scene mutation stays on
`ISceneMemoryStore`; scene providers have no unsupported document-ingestion method. The controller
and agent tool depend on `ISceneMemoryProvider`. Its default implementation delegates to the store,
so replacing storage does not require changing HTTP or agent code.

Register scene services with `AddAgctorSceneMemory(databasePath)` from Extensions. Like
`AddAgctorRagProviders`, it uses `TryAdd` registrations so applications can supply their own implementations.
The existing actor factory's service bridge is confined to the tool's runtime constructor; standalone
callers and tests inject the provider directly.

| Operation | Knowledge RAG | Scene Memory |
|---|---|---|
| Health | `GET /api/rag-providers/health` | `GET /api/scene-memory/health` |
| Query | `POST /api/rag-providers/query` | `POST /api/scene-memory/query` |
| Query scope | `collectionId` | Required `workspaceId` |
| Query input | `query`, optional mode and ranking options | `query` or `entityId`, optional `atSequence` |
| Query outcome | `success`, `providerId`, `message`, `chunks` | `success`, `providerId`, `message`, `result` |

Both health endpoints use the same response mapper. Both query response DTOs inherit the common
outcome fields; domain results remain typed. Example scene request:

```json
{"workspaceId":"your-workspace-id","query":"Where is CupA?","atSequence":1}
```

A successful scene lookup may have `result.status` of `found`, `no_facts`, `not_found` or `ambiguous`;
`success` means retrieval completed, not that facts exist. Valid-body query validation errors return
400 and missing workspaces return 404 with the common outcome fields. ASP.NET request-binding errors
retain its standard validation response. Cancellation propagates from the scene provider.

The earlier `/api/scene-memory/workspaces/{id}/query` route retains its raw `SceneQueryResult` response
and `question` input. It now delegates to the same provider as the new endpoint and agent tool.
The tool accepts `query`, with `question` retained as a compatibility alias. Existing RAG HTTP response
fields and default-provider selection remain unchanged.

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
