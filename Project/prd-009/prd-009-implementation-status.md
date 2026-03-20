# PRD-009: Implementation Status — Trace Timeline Experience Improvements

Tracks progress for **PRD-009** and maps work to UX, API, and test surfaces. **PRD-008** remains the parent effort for historical chat ↔ trace linking.

**Status:** Not started.

## Feature Coverage Matrix

| Area | Expected outcome | Status |
| --- | --- | --- |
| Tree navigation | Expand/collapse, depth collapse, usable layout on wide traces | Not started |
| Search and filter | Find spans by text; quick filters (errors, slow) | Not started |
| Error emphasis | Clear error styling; jump to first/next error | Not started |
| Span detail panel | Structured attributes, duration, copy trace id | Not started |
| Time modes | Relative vs wall-clock (or both) | Not started |
| Chat context header | Shows turn/message context when loaded from chat | Not started |
| External viewer link | Optional configured URL opens trace externally | Not started |
| Performance | Caps, virtualization/chunking, debounced search | Not started |
| States and copy | Partial, not-found, unavailable messages actionable | Not started |
| Docs and tests | Host diagrams updated; unit + integration coverage | Not started |

## Phase Checklist

### Phase 1: Requirements freeze and DTO audit

- [ ] Current timeline payload and UI usage documented.
- [ ] DTO delta list agreed (error, time, attributes, truncation).
- [ ] Caps and truncation policy documented.

### Phase 2: Backend timeline enrichment

- [ ] Error status mapped into timeline DTOs.
- [ ] Truncation metadata returned when applicable.
- [ ] Optional query params implemented if approved.

### Phase 3: Timeline UI — navigation and scale

- [ ] Collapse/expand and depth controls implemented.
- [ ] Large-trace mitigation (virtualization or chunking) implemented or explicitly deferred with rationale.
- [ ] Truncation banner wired to API metadata.

### Phase 4: Search, filter, error navigation

- [ ] Search implemented (client or server per design).
- [ ] Quick filters implemented.
- [ ] Error jump navigation implemented.

### Phase 5: Span detail and time modes

- [ ] Detail panel implemented with sane limits on attribute size.
- [ ] Relative/wall time presentation implemented.

### Phase 6: Chat correlation and external link

- [ ] Context passed from dashboard into timeline load options.
- [ ] External URL template config + UI control when set.

### Phase 7: Quality gate

- [ ] Unit tests for mapping and helpers.
- [ ] Integration tests for timeline API contracts.
- [ ] Full solution build; unit tests; integration tests.

## Expected File Touchpoints

### Host UI

- `AgctorSDK.Host/Pages/Shared/Components/TraceTimeline/Default.cshtml`
- `AgctorSDK.Host/wwwroot/js/dashboard/codegraph-page.js` (context options, integration)
- Timeline-specific JS/CSS under `wwwroot` (if split from monolith)

### Host API and models

- `AgctorSDK.Host/Controllers/VisualizationController.cs`
- `AgctorSDK.Host/Models/ApiModels.cs` (or timeline DTO types)
- Trace/timeline mapping services under `AgctorSDK.Host/Services/`

### Configuration

- `AgctorSDK.Host/appsettings.json` (external trace URL template, caps)

### Tests and docs

- `AgctorSDK.Core.Tests` / `AgctorSDK.Host.IntegrationTests` (as appropriate)
- `AgctorSDK.Host/docs/endpoints-diagram.*`
- `AgctorSDK.Host/docs/class-diagram.*`

## Verification Notes

- Update this file as phases complete; link PRs or commits if your team uses that convention.
- If DTOs change, verify **PRD-008** clients (chat trace loader) still work without changes or version the API.

## Optional Follow-ups

- Server-side full-text or tag-indexed search across traces (cross-trace).
- User preferences for default time mode and default filters (would need a persistence story).
- Correlation with logs (trace id → log query) when logging integration exists.
