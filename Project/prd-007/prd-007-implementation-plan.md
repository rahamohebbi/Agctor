# PRD-007: Implementation Plan — CodeGraph UI Component Inventory

This plan turns the CodeGraph UI inventory into incremental Razor component extraction tasks.

**Status:** Complete — see [`prd-007-implementation-status.md`](./prd-007-implementation-status.md) for verification details.

## Phase 1: Component boundaries

| Step | Action |
| --- | --- |
| 1.1 | Keep `CodeGraph.cshtml` as page-level shell and API orchestration entry. |
| 1.2 | Confirm boundaries for `EmbeddingStorePanel`, `AgentChatPanel`, `ActorTreePanel`, `FilePreviewModal`, `EmbeddingDebugPanel`, and `RawJsonPanel`. |
| 1.3 | Keep `TraceTimeline` as existing standalone view component. |

## Phase 2: Extract high-value components

| Step | Action |
| --- | --- |
| 2.1 | Extract `EmbeddingStorePanel` (store summary and index trigger UI). |
| 2.2 | Extract `AgentChatPanel` (session + agent chat workflow). |
| 2.3 | Extract `ActorTreePanel` and connect file-node actions to `FilePreviewModal`. |

## Phase 3: Extract diagnostics components

| Step | Action |
| --- | --- |
| 3.1 | Extract `EmbeddingDebugPanel` (vectors table + 2D preview). |
| 3.2 | Extract `RawJsonPanel` (debug details payload). |
| 3.3 | Verify state transitions still render correctly for loading, not-active, and error states. |

## Phase 4: Cleanup

| Step | Action |
| --- | --- |
| 4.1 | Move component-specific JavaScript into scoped modules. |
| 4.2 | Keep comments short and focused where behavior is not obvious. |
| 4.3 | Add/refresh tests and documentation for component boundaries. |
