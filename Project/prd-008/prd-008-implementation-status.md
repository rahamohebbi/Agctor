# PRD-008: Implementation Status — Historical Trace Timeline for Chat Sessions

This document tracks implementation progress for `PRD-008` and maps delivered work back to the planned backend, actor, API, and frontend surfaces.

**Status:** Not started.

## Feature Coverage Matrix

| Area | Expected outcome | Status |
| --- | --- | --- |
| Durable prompt-to-trace correlation | Each historical prompt or turn can be mapped back to a stored trace reference. | Not started |
| Actor-owned trace index | Session-scoped actor flow owns trace correlation updates. | Not started |
| OpenTelemetry historical retrieval | Trace timelines can be reloaded from the configured trace backend. | Not started |
| Transcript trace metadata | Session transcript APIs expose stable ids and trace-aware metadata. | Not started |
| Dashboard historical trace selection | Users can click historical prompts or responses to load the Trace Timeline. | Not started |
| Turn-level default with message drill-down | Turn-level selection works first, with message-specific trace selection only when useful. | Not started |
| Legacy session compatibility | Older sessions without trace metadata still render cleanly. | Not started |
| Build and test verification | All affected projects build; unit and integration tests pass. | Not started |

## Phase Checklist

### Phase 1: Contracts and correlation model

- [ ] Trace correlation models defined.
- [ ] Store interfaces for trace links defined.
- [ ] Transcript DTO updates agreed and documented.
- [ ] Decision finalized on extending `SessionTurn` versus using side records.

### Phase 2: Actor-owned trace indexing

- [ ] Session actor flow persists prompt-to-trace correlation.
- [ ] Correlation writes are append-only and deterministic.
- [ ] Live request path still returns immediate `traceId`.
- [ ] Actor ownership is documented in code comments where behavior is non-obvious.

### Phase 3: OpenTelemetry historical retrieval

- [ ] Historical trace query abstraction implemented.
- [ ] OpenTelemetry-backed trace fetch wired into the timeline flow.
- [ ] Timeline mapping from backend spans verified.
- [ ] Not-found and backend-unavailable behavior defined and tested.

### Phase 4: Session transcript and visualization APIs

- [ ] Session transcript endpoint returns trace-aware metadata.
- [ ] Optional turn-level or message-level trace lookup endpoints added if needed.
- [ ] Existing direct `trace/{traceId}/timeline` route remains supported.
- [ ] Host endpoint docs updated.

### Phase 5: Dashboard chat and Trace Timeline UX

- [ ] Historical turn trace affordance added.
- [ ] Optional message-level drill-down added where trace ids differ.
- [ ] Selected message or turn highlight added.
- [ ] Timeline loading and empty states updated.
- [ ] Live prompt auto-load behavior preserved.

### Phase 6: Quality gate

- [ ] Unit tests added for correlation and mapping logic.
- [ ] Integration tests added for historical reload scenarios.
- [ ] Regression coverage added for legacy sessions.
- [ ] Build completed for all affected projects.
- [ ] Unit tests completed.
- [ ] Integration tests completed.

## Expected File Touchpoints

### Core and actor model

- `AgctorSDK.Core/Interfaces/ISessionStore.cs`
- `AgctorSDK.Core/Sessions/Models/SessionTurn.cs`
- `AgctorSDK.Core/Utils/ActivityTracking/IActivityTracker.cs`
- `AgctorSDK.Agents/Agents/SessionCoordinatorAgent.cs`
- `AgctorSDK.Agents/Agents/SessionMemoryAgent.cs`

### Host APIs and services

- `AgctorSDK.Host/Controllers/AgentsController.cs`
- `AgctorSDK.Host/Controllers/ChatSessionsController.cs`
- `AgctorSDK.Host/Controllers/VisualizationController.cs`
- `AgctorSDK.Host/Services/MessageDispatcher.cs`
- `AgctorSDK.Core/Utils/ActivityTracking/OpenTelemetry/OpenTelemetryActivityTracker.cs`

### Dashboard UI

- `AgctorSDK.Host/Pages/Dashboard/CodeGraph.cshtml`
- `AgctorSDK.Host/Pages/Shared/Components/AgentChat/Default.cshtml`
- `AgctorSDK.Host/Pages/Shared/Components/TraceTimeline/Default.cshtml`
- `AgctorSDK.Host/wwwroot/js/dashboard/codegraph-page.js`

### Tests and docs

- `AgctorSDK.Core.Tests`
- `AgctorSDK.Host.IntegrationTests`
- `AgctorSDK.Host/docs/architecture-diagram.*`
- `AgctorSDK.Host/docs/class-diagram.*`
- `AgctorSDK.Host/docs/endpoints-diagram.*`
- `AgctorSDK.Host/docs/dependencies-diagram.*`

## Verification Notes

- Keep this file updated as phases move from design to implementation.
- When a phase is completed, record the main files changed and the tests that validated it.
- If implementation diverges from the plan, update both this file and the `prd-008` plan so the documentation stays aligned.

## Optional Follow-ups

- Add trace filtering or search within a long session.
- Add export or deep-link support for a selected historical trace.
- Add richer trace summaries in the transcript list if the base flow proves useful.
