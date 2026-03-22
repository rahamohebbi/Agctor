# PRD-009: Implementation Status — Trace Timeline Experience Improvements

Tracks **PRD-009** against the codebase. **PRD-008** remains the parent effort for historical chat ↔ trace linking and durable timeline persistence.

**PRD folder status:** **Specification closed** (2026-03-20). Use this file when resuming enhancement work.

---

## Already shipped (baseline — mostly PRD-008 + current Host)

These exist in the product today; they **satisfy early “see a trace” needs** but are **not** the full PRD-009 scope.

| Capability | Where / notes |
| --- | --- |
| Load timeline by `traceId` | `GET /api/Visualization/trace/{traceId}/timeline` — `VisualizationController` |
| Historical snapshot store | `ITraceTimelineStore` / `SqliteTraceTimelineStore`; `MessageDispatcher` saves timelines |
| Dashboard widget | `TraceTimeline` ViewComponent — `Pages/Shared/Components/TraceTimeline/Default.cshtml` |
| SVG timeline + event list | In-component JS: duration bars, depth-based coloring, `HasResult`, start offset / duration text |
| Chat → trace handoff | `codegraph-page.js` calls `agctorTraceTimeline.load(..., { selectionLabel, emptyMessage, errorMessage })` |
| Optional external viewer | `TraceTimelineResponse.ExternalViewerUrl` (when visualization service provides a template) |
| Timeline DTOs (current) | `TraceTimelineResponse`, `TraceTimelineEventDto` in `AgctorSDK.Host/Models/ApiModels.cs` — id, parent, label, sequence, depth, timestamps, offsets, duration, `HasResult` |

---

## PRD-009 enhancement backlog (not started)

| Area | Expected outcome | Status |
| --- | --- | --- |
| Tree navigation | Expand/collapse, depth collapse, dedicated expand-all UX | **Backlog** |
| Search and filter | Text search; quick filters (errors, slow spans) | **Backlog** |
| Error emphasis | Error status on DTOs + UI; jump to first/next error | **Backlog** — `TraceTimelineEventDto` has no explicit error/status field yet |
| Span detail panel | Rich attributes, truncation, copy trace id in-panel | **Backlog** — details are a simple list, not a structured panel |
| Time modes | Relative vs wall-clock toggle | **Backlog** — wall time shown per event; no mode toggle |
| Chat context header | Compact session / turn / “message vs turn trace” chrome beyond `selectionLabel` | **Backlog** — partial: label only |
| External viewer link | UI affordance if URL present | **Partial** — URL on DTO; confirm dashboard exposes “open external” if desired |
| Performance | Caps, truncation metadata, virtualization, debounced search | **Backlog** |
| States and copy | Partial / stale / retry copy per PRD-009 §9 | **Backlog** |
| Docs and tests | Host diagrams + tests for new DTO/query behavior | **Backlog** for PRD-009-specific changes |

---

## Phase checklist (for when work restarts)

### Phase 1: Requirements freeze and DTO audit

- [x] PRD-009 spec and UI inventory documented (this folder).
- [ ] Live timeline payload vs PRD-009 §10 delta documented in a short ADR or ticket.
- [ ] Caps and truncation policy agreed.

### Phase 2: Backend timeline enrichment

- [ ] Error status / message on `TraceTimelineEventDto` when source data allows.
- [ ] Truncation metadata on `TraceTimelineResponse` when capped.
- [ ] Optional query params (`includeAttributes`, `maxSpans`, etc.) if needed.

### Phase 3: Timeline UI — navigation and scale

- [ ] Collapse/expand and depth controls.
- [ ] Large-trace mitigation (virtualization or chunking) or explicit deferral note in UI.
- [ ] Truncation banner wired to API metadata.

### Phase 4: Search, filter, error navigation

- [ ] Search (client or server per Phase 1 decision).
- [ ] Quick filters.
- [ ] Error jump navigation.

### Phase 5: Span detail and time modes

- [ ] Detail panel with capped attributes.
- [ ] Relative/wall time presentation per spec.

### Phase 6: Chat correlation and external link

- [ ] Richer context contract (session/turn/message) if beyond `selectionLabel`.
- [ ] Config + visible “Open in external viewer” when template is set.

### Phase 7: Quality gate

- [ ] Unit tests for mapping, truncation, filters.
- [ ] Integration tests for timeline API contracts.
- [ ] Full solution build; unit tests; integration tests.

---

## Expected file touchpoints (unchanged)

### Host UI

- `AgctorSDK.Host/Pages/Shared/Components/TraceTimeline/Default.cshtml`
- `AgctorSDK.Host/wwwroot/js/dashboard/codegraph-page.js`

### Host API and models

- `AgctorSDK.Host/Controllers/VisualizationController.cs`
- `AgctorSDK.Host/Models/ApiModels.cs`

### Configuration

- `AgctorSDK.Host/appsettings.json` (external trace URL template, future caps)

### Tests and docs

- `AgctorSDK.Core.Tests` / `AgctorSDK.Host.IntegrationTests` as appropriate
- `AgctorSDK.Host/docs/*-diagram.*`

---

## Verification notes

- When DTOs or routes change, re-verify **PRD-008** chat trace loaders still work or version the API.
- Update backlog rows above as features land; link PRs/commits if the team tracks that way.
