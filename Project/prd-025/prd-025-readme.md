# PRD-025 — External RAG providers & dashboard

**Status:** Implemented (v1) — LightRAG + Cognee adapters, dashboard, Project Memory wiring.

## Summary

Agctor gains a **pluggable RAG provider layer** so `LlmNode` `contextStrategy: rag | graph_rag` (and future strategies) retrieve context from **external** systems instead of loading all on-disk markdown. Operators manage providers from a new **`/Dashboard/RagProviders`** page with the same UX patterns as **Actor runtime** (catalog cards, config form, Docker install/start/stop, terminal command panel, health).

Canonical project files under `.agctor/` remain **source of truth**; RAG indexes are **derived views** (optional sync from workspace).

## Documents

| File | Purpose |
| --- | --- |
| [prd-025-agctor-prd.md](./prd-025-agctor-prd.md) | Adapter architecture, API, config, integration with Project Memory & scenario flow |
| [prd-025-ux-spec.md](./prd-025-ux-spec.md) | RagProviders dashboard UX (mirrors Actor runtime) |
| [prd-025-implementation-plan.md](./prd-025-implementation-plan.md) | Phased delivery, file map, tests |

## v1 scope

| In scope | Out of scope (deferred) |
| --- | --- |
| `IRagProviderAdapter` + REST/MCP transports | Agctor-native Actor-model RAG engine |
| LightRAG + Cognee catalog entries + Docker sidecars | PageIndex, RAGFlow, ColPali (v2+) |
| Dashboard: select, configure, install, run, stop | SaaS OAuth flows (design only in PRD) |
| Wire `PersonMemoryMarkdownContextBuilder` / `LlmNode` to active provider | Full bi-directional sync pipeline (ingest actor) |
| `appsettings.User.json` persistence | Multi-tenant RAG isolation |

**Host docs:** [AgctorSDK.Host/docs/rag-providers.md](../../AgctorSDK.Host/docs/rag-providers.md)

## Related PRDs

| PRD | Relationship |
| --- | --- |
| **PRD-012** | Actor runtime dashboard — UX and Docker CLI patterns to reuse |
| **PRD-013** | Project Memory, `contextStrategy`, semantic retrieval (§15.3) |
| **PRD-014 / 024** | `LlmNode.config.contextStrategy`, scenario flow |
| **PRD-020** | Actor/tool patterns; future `RagOrchestratorActor` |
| **PRD-023** | Visual memory stays separate; may share provider infra later |

## Recommended architecture (one line)

**Port/adapter:** `IRagProviderAdapter` (semantic ops) + `IRagTransport` (REST or MCP) + `RagProviderCatalog` (dashboard copy) + `IRagProviderDockerService` (sidecar lifecycle).
