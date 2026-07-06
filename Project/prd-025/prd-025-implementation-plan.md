# PRD-025: Implementation plan — External RAG providers

**Status:** v1 complete (Phases 1–6).  
**Prerequisite:** PRD-012 Actor runtime Docker/terminal patterns shipped.

---

## Phase 1 — Core contract & catalog

| Step | Action | Location |
| --- | --- | --- |
| 1.1 | Add PRD-025 docs | `Project/prd-025/` |
| 1.2 | `IRagProviderAdapter`, request/result records, `RagQueryMode` enum | `AgctorSDK.Core/Rag/` |
| 1.3 | `RagProviderCatalog` + `RagProviderConfigSchema` (mirror `ActorRuntimeCatalog`) | `AgctorSDK.Core/Rag/` |
| 1.4 | `IRagProviderAdapterFactory` + DI registration | `AgctorSDK.Extensions/DependencyInjection/` |
| 1.5 | Unit tests: catalog, schema fields, factory resolution | `AgctorSDK.Core.Tests/Rag/` |

---

## Phase 2 — Transports & v1 adapters

| Step | Action | Location |
| --- | --- | --- |
| 2.1 | `IRagTransport`, `RestRagTransport`, `McpHttpRagTransport` | `AgctorSDK.Core/Rag/Transport/` |
| 2.2 | `LightRagAdapter` — map query/ingest/health to LightRAG REST | `AgctorSDK.Extensions/Rag/Providers/` |
| 2.3 | `CogneeAdapter` — map query/ingest to MCP tools | `AgctorSDK.Extensions/Rag/Providers/` |
| 2.4 | `RagContextService` — orchestrates query + appendix formatting | `AgctorSDK.Core/ProjectMemory/Rag/` |
| 2.5 | Mock-server unit tests for both adapters | `AgctorSDK.Core.Tests/Rag/` |

---

## Phase 3 — Docker & settings

| Step | Action | Location |
| --- | --- | --- |
| 3.1 | `docker/rag-providers/docker-compose.yml` (lightrag, cognee-mcp, volumes) | `docker/rag-providers/` |
| 3.2 | `IRagProviderDockerService` + impl (clone `ActorRuntimeDockerService`) | `AgctorSDK.Host/Services/` |
| 3.3 | `IUserRagSettingsService` — read/write `Agctor:Rag:*` in `appsettings.User.json` | `AgctorSDK.Host/Services/` |
| 3.4 | `RagProviderConfigBuilder` | `AgctorSDK.Host/Services/` |
| 3.5 | Integration test: Docker status API (skip if no Docker) | `AgctorSDK.Host.IntegrationTests/` |

---

## Phase 4 — Dashboard & API

| Step | Action | Location |
| --- | --- | --- |
| 4.1 | `RagProvidersController` — GET/PUT/health/docker/query | `AgctorSDK.Host/Controllers/` |
| 4.2 | `RagProvidersDashboardService` | `AgctorSDK.Host/Services/` |
| 4.3 | `Pages/Dashboard/RagProviders.cshtml` + code-behind | `AgctorSDK.Host/Pages/Dashboard/` |
| 4.4 | `RagProvidersDashboardViewComponent` + `Default.cshtml` | `AgctorSDK.Host/` |
| 4.5 | `rag-providers-dashboard.js` | `AgctorSDK.Host/wwwroot/js/dashboard/` |
| 4.6 | Nav links in `_Layout.cshtml` | `AgctorSDK.Host/Pages/Shared/` |
| 4.7 | Extend terminal context presets for `rag-provider` | `TerminalController` or preset map |

---

## Phase 5 — Project Memory wiring

| Step | Action | Location |
| --- | --- | --- |
| 5.1 | Replace stub in `PersonMemoryMarkdownContextBuilder` for `rag` / `graph_rag` | `AgctorSDK.Core/ProjectMemory/Tools/` |
| 5.2 | Pass `ragProviderId`, `ragTopK` from `PersonMemoryContextTool` params | `AgctorSDK.Tools/.../PersonMemoryContextTool.cs` |
| 5.3 | Playground builder + scenario flow runner | `PlaygroundPersonQueryContextBuilder.cs`, `ScenarioFlowPersonaLlmRunner.cs` |
| 5.4 | Scenarios UI hint + optional node config fields | `Scenarios.cshtml`, `scenarios-page.js` |
| 5.5 | Integration tests: rag strategy with mock provider | `AgctorSDK.Host.IntegrationTests/` |

---

## Phase 6 — Docs & quality gate

| Step | Action | Location |
| --- | --- | --- |
| 6.1 | Host docs: endpoints + architecture delta | `AgctorSDK.Host/docs/` |
| 6.2 | Cross-link PRD-013 §15.3 | `Project/prd-013/prd-013-readme.md` |
| 6.3 | `dotnet build` + unit + integration tests | CI / local |

---

## File map (new)

| File | Purpose |
| --- | --- |
| `AgctorSDK.Core/Rag/IRagProviderAdapter.cs` | Provider contract |
| `AgctorSDK.Core/Rag/RagProviderCatalog.cs` | Dashboard catalog |
| `AgctorSDK.Core/Rag/RagProviderConfigSchema.cs` | Form fields + Docker service names |
| `AgctorSDK.Extensions/Rag/Providers/LightRagAdapter.cs` | LightRAG REST |
| `AgctorSDK.Extensions/Rag/Providers/CogneeAdapter.cs` | Cognee MCP |
| `AgctorSDK.Core/ProjectMemory/Rag/RagContextService.cs` | Appendix builder |
| `AgctorSDK.Host/Services/RagProviderDockerService.cs` | Docker lifecycle |
| `AgctorSDK.Host/Controllers/RagProvidersController.cs` | REST API |
| `docker/rag-providers/docker-compose.yml` | Sidecar stack |

---

## v2 backlog (post v1)

| Item | Notes |
| --- | --- |
| PageIndex adapter | MCP `@pageindex/mcp` or cloud API |
| RAGFlow adapter | REST + built-in MCP |
| ColPali / multimodal | Separate `RagQueryMode.Visual` |
| `RagOrchestratorActor` | Async ingest, Actor-model native RAG |
| Workspace sync job | Push `people/**/*.md` to active provider |
| SaaS OAuth | Azure AI Search, Pinecone, etc. |
| Per-scenario `rag.yaml` | Override default provider |

---

## Estimated effort

| Phase | Size |
| --- | --- |
| 1–2 | Core + adapters (~3–4 days) |
| 3–4 | Docker + dashboard (~3–4 days) |
| 5 | Project Memory wiring (~2 days) |
| 6 | Tests + docs (~1 day) |

**Total v1:** ~2 weeks focused engineering.
