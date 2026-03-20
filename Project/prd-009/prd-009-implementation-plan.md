# PRD-009: Implementation Plan — Trace Timeline Experience Improvements

This plan breaks **PRD-009** into data, API, UI, and quality work so timeline debugging scales with real agent traces. Depends on **PRD-008** for chat ↔ trace selection and durable correlation.

**Status:** Draft.

## Phase 1: Requirements freeze and DTO audit

| Step | Action | Location |
| --- | --- | --- |
| 1.1 | Inventory current timeline JSON shape and what the UI actually renders today. | `AgctorSDK.Host/Models/*`, `TraceTimeline` component, `codegraph-page.js` |
| 1.2 | List minimal DTO additions for: error flag, duration, timestamps, attributes subset, truncation metadata. | Core/Host models |
| 1.3 | Decide caps: max spans returned, max attribute length, max search results. | PRD + Host options |

## Phase 2: Backend timeline enrichment (optional fields)

| Step | Action | Location |
| --- | --- | --- |
| 2.1 | Map span status (OK / error) and error message when available from tracker/store. | `VisualizationController`, trace mapping services |
| 2.2 | Ensure stable span ids for selection and deep links. | Timeline DTOs |
| 2.3 | Add response metadata when payload is truncated: `truncated`, `totalSpanCount`, `returnedSpanCount`. | API models |
| 2.4 | Optional query params: `includeAttributes`, `maxDepth` (if tree is server-filtered). | `VisualizationController` |

## Phase 3: Trace Timeline UI — navigation and scale

| Step | Action | Location |
| --- | --- | --- |
| 3.1 | Implement expand/collapse all, collapse to depth, remember expansion state per trace session. | `TraceTimeline/Default.cshtml`, timeline JS |
| 3.2 | Add zoom/pan or horizontal layout improvements **if** current renderer supports it; otherwise document deferral. | Same |
| 3.3 | Virtualize or chunk-render long lists; debounce search input. | Timeline JS |
| 3.4 | Show truncation banner when API reports truncated results. | Timeline UI |

## Phase 4: Search, filter, and error navigation

| Step | Action | Location |
| --- | --- | --- |
| 4.1 | Client-side search across name + selected attributes (server-side only if payload is partial). | Timeline JS |
| 4.2 | Quick filters: errors only, duration threshold. | Timeline JS |
| 4.3 | “First error” / “next error” keyboard or button affordances. | Timeline JS |

## Phase 5: Span detail and time modes

| Step | Action | Location |
| --- | --- | --- |
| 5.1 | Span detail panel (drawer or inline) with formatted attributes and copy trace id. | `TraceTimeline` |
| 5.2 | Relative vs wall-clock toggle; format tooltips consistently. | Timeline JS |

## Phase 6: Chat correlation header and external link

| Step | Action | Location |
| --- | --- | --- |
| 6.1 | Pass selection context from `codegraph-page.js` into timeline `load(..., options)` (session/turn/message labels). | Dashboard JS + `TraceTimeline` |
| 6.2 | Host config: optional external trace URL template; render “Open externally” when set. | `appsettings`, small options class, Razor/JS |

## Phase 7: Quality gate

| Step | Action | Location |
| --- | --- | --- |
| 7.1 | Unit tests for mapping, truncation metadata, error detection helpers. | `AgctorSDK.Core.Tests` / Host tests as appropriate |
| 7.2 | Integration test: timeline endpoint returns enriched fields and obeys caps. | `AgctorSDK.Host.IntegrationTests` |
| 7.3 | Build all projects; unit tests; integration tests. | Solution |

## Dependency Order

- Phase 1 before 2 (avoid reactive DTO churn).
- Phase 2 before 4 if search must be server-side for partial loads; else 3–4 can proceed with client-side search on full payload.
- Phase 5 can parallelize with 3–4 once basic selection events exist.
- Phase 6 integrates with existing chat trace loader from PRD-008.

## Documentation Updates

- `AgctorSDK.Host/docs/endpoints-diagram.*` — new query params or fields.
- `AgctorSDK.Host/docs/class-diagram.*` — DTOs and services.
- `AgctorSDK.Host/docs/architecture-diagram.*` — only if new services appear.

## Notes

- Prefer **incremental** delivery: error highlight + search often deliver the most value first.
- Keep **Actor Model** ownership unchanged: no new session state required for pure timeline UX unless we add user preferences (then consider Host-only or future actor-backed settings).
