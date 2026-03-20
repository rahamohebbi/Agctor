# PRD-007 — Implementation status

Verification against `prd-007-codegraph-ui-elements.md` and `prd-007-implementation-plan.md` (as of repo state when this file was updated).

## Inventory vs code

| Feature (inventory) | Status | Implementation notes |
| --- | --- | --- |
| Page state shell (loading / not active / fetch error) | Done | `Pages/Dashboard/CodeGraph.cshtml` + `wwwroot/js/dashboard/codegraph-page.js` |
| Embedding store panel | Done | `EmbeddingStoreViewComponent` + `Pages/Shared/Components/EmbeddingStore/Default.cshtml` |
| Chat with agents | Done | `AgentChatViewComponent` + `AgentChat/Default.cshtml` |
| Trace timeline | Done | `TraceTimelineViewComponent` (mounted under chat via JS); trace data from activity APIs |
| Actor tree explorer | Done | `ActorTreeViewComponent` + `ActorTree/Default.cshtml` (`window.agctorActorTree`) |
| File preview modal | Done | Embedded in `ActorTree/Default.cshtml` (PRD optional separate `FilePreviewModal` — behavior satisfied) |
| Embedding vectors debug | Done | `EmbeddingDebugViewComponent` |
| Raw JSON debug | Done | `RawJsonViewComponent` |

## Phases

| Phase | Status |
| --- | --- |
| 1 — Boundaries / keep TraceTimeline | Done |
| 2 — EmbeddingStore, AgentChat, ActorTree + modal | Done |
| 3 — EmbeddingDebug, RawJson, state transitions | Done |
| 4.1 — Scoped JS module | Done — `wwwroot/js/dashboard/codegraph-page.js` referenced from `CodeGraph.cshtml` |
| 4.2 — Short comments | Done — file header on JS; Razor `@*` where useful |
| 4.3 — Tests & docs | Done — `CodeGraphDashboardPageIntegrationTests`; Host `class-diagram.mmd` ViewComponents |

## Automated checks

- Solution build, unit tests, and integration tests should pass after changes touching this area (see workspace build/test rule).

## Optional follow-ups (not required by PRD text)

- Split **File preview modal** into its own ViewComponent if other pages need the same modal markup.
- Further split `codegraph-page.js` into ES modules if the script grows again.
