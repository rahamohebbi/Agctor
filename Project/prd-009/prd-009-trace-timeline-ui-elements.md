# PRD-009: Trace Timeline UI and API Inventory

## Purpose

List UI elements, behaviors, and API/DTO hooks needed for **PRD-009** timeline improvements so frontend and backend stay aligned. Complements **PRD-008** chat trace affordances.

**Related:** [PRD-008 Chat Trace UI](../prd-008/prd-008-chat-trace-ui-elements.md) · [PRD-009 readme](./prd-009-readme.md)

## Current implementation snapshot (baseline)

Today the timeline is implemented as a **single ViewComponent** with inline script — not yet split into the named subcomponents below:

- **Toolbar / search / filters:** not present.
- **Chart + details:** `TraceTimeline/Default.cshtml` — SVG chart (`data-role="chart"`) and stacked event cards (`data-role="details"`).
- **API:** `TraceTimelineResponse` / `TraceTimelineEventDto` — see `ApiModels.cs`; no `truncated`, no per-span error payload yet.

Use this section as the **Phase 1.1** baseline when starting PRD-009 implementation.

## Scope

- **In-scope:** timeline panel internals (tree, toolbar, detail, search), enrichment fields on timeline responses, configuration for external trace URLs.
- **Out-of-scope:** redesign of full CodeGraph page layout, new chat transcript row types (unless a small header contract is needed).

## Component Grouping Table

| UI or API element | Self-contained component or contract? |
| --- | --- |
| **Timeline toolbar** (search, filter chips, expand/collapse, time mode) | **Yes** — keep as a focused strip above the tree. |
| **Trace tree / Gantt row** (existing visualization host) | **Partial** — enhance in place; avoid forked duplicate components. |
| **Span row** (single span with status, duration bar, error glyph) | **Yes** — repeatable row with selection and keyboard focus. |
| **Span detail panel** | **Yes** — drawer or collapsible region bound to selected span id. |
| **Error navigation** (first/next error) | **Yes** — small control group tied to filtered span list. |
| **Truncation banner** | **Yes** — driven by API `truncated` metadata. |
| **Context header** (session/turn/message when opened from chat) | **Yes** — fed via `load(traceId, { context })` options from dashboard. |
| **External viewer link** | **Yes** — optional anchor; hidden if template unset. |
| **Timeline response contract** (spans + error fields + truncation + optional attributes) | **Yes** — explicit API/DTO version or optional fields. |

## Proposed UI Set

- `TraceTimelineToolbar`: search input, filter toggles, expand/collapse, time mode.
- `TraceSpanRow`: name, duration, status, indent, error highlight.
- `SpanDetailPanel`: attributes table, copy actions, raw fragment on demand.
- `TraceTruncationBanner`: counts and “load more” or documentation link if full load unsupported.
- `TraceContextHeader`: short session/turn/message label + trace id copy.
- `ExternalTraceLink`: configured URL pattern + trace id interpolation.

## Interaction Rules

- Search narrows visible rows; clearing search restores prior expand state where reasonable.
- “Errors only” implies non-error spans hidden unless they are ancestors of an error (implementation choice: show ancestor path vs flat errors only — document chosen behavior).
- Selecting a span updates detail panel; second click or Escape clears selection (define consistently).
- External link opens in new tab; never embed secrets in the URL template beyond trace id.

## API / DTO Fields (illustrative)

Document final names at implementation time; examples:

- Per span: `spanId`, `name`, `startTimeUnixMs`, `endTimeUnixMs`, `durationMs`, `status` (`ok` \| `error`), `errorMessage?`, `attributes?` (map, capped)
- Response: `traceId`, `truncated`, `totalSpanCount?`, `returnedSpanCount?`
- Query: `includeAttributes=true|false`, `maxSpans=N` (if server-side cap)

## Timeline States (extends PRD-008)

- **Partial** — some spans omitted per cap; banner visible.
- **Stale** — optional future: snapshot timestamp older than configurable threshold.

## Notes

- Prefer **client-side** search when full tree is already loaded; use **server-side** filtering when responses are paginated or truncated.
- Keep accessibility in mind: focus order, ARIA for selected span, visible error state not only color.
