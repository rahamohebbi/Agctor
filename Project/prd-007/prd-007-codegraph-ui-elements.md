# PRD-007: CodeGraph UI Component Inventory

## Purpose

Define a clear inventory of CodeGraph page UI elements and group related elements into functional components. This is used to guide a cleaner Razor component structure.

## Scope

- In-scope: UI elements currently rendered on `Dashboard/CodeGraph`, grouped by functionality.
- Out-of-scope: backend API changes and behavioral changes.

## Component Grouping Table

| UI element | Self-contained Razor component? |
| --- | --- |
| **CodeGraph page state shell** (loading spinner, "CodeGraph not active" state, generic load-failure state) | **No** - keep page-level orchestration in `CodeGraph.cshtml`; state shell is tied to initial page bootstrap and API fetch lifecycle. |
| **Embedding store** (vector count, state, graph/index versions, last indexed, last error, `Index now` action, status message) | **Yes** - create a focused component (for example `EmbeddingStorePanel`) to isolate indexing and store status UI. |
| **Chat with agents** (session picker, new session, active session label, agent picker, prompt input, send action, status, transcript list, completion banner) | **Yes** - create a dedicated component (for example `AgentChatPanel`) because this is a standalone user workflow with its own state and events. |
| **Trace timeline** (timeline host and trace rendering) | **Yes** - already effectively self-contained via `TraceTimeline` view component; keep as its own component boundary. |
| **Actor tree explorer** (Solution -> Project -> File -> Class -> Method hierarchy rendering and file-node actions) | **Yes** - create a standalone component (for example `ActorTreePanel`) for recursive rendering and refresh logic. |
| **File preview modal** (modal shell, close actions, file path/title, file content area) | **Yes** - create a reusable component (for example `FilePreviewModal`) so tree and future pages can reuse file preview behavior. |
| **Embedding vectors debug** (load vectors action, vectors table, 2D scatter preview) | **Yes** - isolate into a debug-focused component (for example `EmbeddingDebugPanel`) to keep diagnostics separate from primary UX. |
| **Raw JSON debug** (expandable details + formatted context payload) | **Yes** - move to a small reusable diagnostics component (for example `RawJsonPanel`). |

## Proposed Initial Razor Component Set

- `EmbeddingStorePanel`: owns embedding store summary and index trigger controls.
- `AgentChatPanel`: owns chat session lifecycle, chat input, replies, and chat status indicators.
- `ActorTreePanel`: owns actor tree rendering and node interaction wiring.
- `FilePreviewModal`: owns file-content preview modal state and rendering.
- `EmbeddingDebugPanel`: owns vector debug table and visualization.
- `RawJsonPanel`: owns raw context payload rendering.
- `TraceTimeline`: keep existing standalone timeline component.

## Notes

- **Chat send status** (`codegraph-page.js`): all agents use the same rich progress card (elapsed clock, phased Context → Search → LLM → last step, amber “why it waits” callout). Profiles: `query-agent` ends with **Answer**; `coder-agent` / `refactor-agent` with **Edit**; any other id uses **Reply** and generic copy.
- Keep page-level data orchestration in `CodeGraph.cshtml` initially.
- Extract component-specific rendering and event handling first, then progressively move per-component JavaScript to scoped modules.
- This grouping follows the existing functionality boundaries visible on the page today and reduces duplication while keeping each component focused.

**Implementation tracking:** [`prd-007-implementation-status.md`](./prd-007-implementation-status.md).
