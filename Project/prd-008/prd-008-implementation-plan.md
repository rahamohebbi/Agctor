# PRD-008: Implementation Plan — Historical Trace Timeline for Chat Sessions

This plan breaks `PRD-008` into backend, actor, API, and dashboard work so trace history can be reopened for any historical prompt inside a chat session.

**Status:** Draft.

## Phase 1: Contracts and correlation model

| Step | Action | Location |
| --- | --- | --- |
| 1.1 | Define durable trace-correlation models for chat turns and messages, including primary turn trace id plus optional request/response trace ids. | `AgctorSDK.Core/Sessions/*` or a new trace-history folder under `AgctorSDK.Core` |
| 1.2 | Add store interfaces for trace link persistence and lookup without storing raw span payloads in SQLite. | `AgctorSDK.Core/Interfaces/*` |
| 1.3 | Decide whether `SessionTurn` should be extended directly or whether a dedicated side record should be added. Prefer a dedicated side record if it keeps transcript storage simple. | `AgctorSDK.Core/Sessions/Models/SessionTurn.cs` and related trace-link models |
| 1.4 | Define transcript DTO updates so the UI receives stable ids, turn grouping, and trace availability metadata. | `AgctorSDK.Host/Models/ApiModels.cs` and session transcript models |

## Phase 2: Actor-owned trace indexing

| Step | Action | Location |
| --- | --- | --- |
| 2.1 | Extend the session actor workflow so prompt completion records durable prompt-to-trace correlation metadata. | `AgctorSDK.Agents/Agents/SessionCoordinatorAgent.cs` and `SessionMemoryAgent.cs` |
| 2.2 | If needed, introduce a focused per-session trace index actor instead of overloading session memory responsibilities. | `AgctorSDK.Agents/Agents/*Trace*Agent.cs` |
| 2.3 | Make correlation append-only and deterministic so each prompt and response can be reloaded consistently. | Session actor implementation and Host persistence integration |
| 2.4 | Ensure live request flow still returns immediate `traceId` while also persisting the historical lookup record. | `AgctorSDK.Host/Services/MessageDispatcher.cs` and `AgctorSDK.Host/Controllers/AgentsController.cs` |

## Phase 3: OpenTelemetry-backed historical trace retrieval

| Step | Action | Location |
| --- | --- | --- |
| 3.1 | Introduce a trace query abstraction responsible for reading historical spans from the configured OpenTelemetry backend. | `AgctorSDK.Core` contract and `AgctorSDK.Host/Services/*Trace*` implementation |
| 3.2 | Replace the current placeholder `GetTraceActivitiesAsync(traceId)` behavior with a real backend-backed implementation or a dedicated query service used by the controller. | `AgctorSDK.Core/Utils/ActivityTracking/OpenTelemetry/OpenTelemetryActivityTracker.cs` and related services |
| 3.3 | Map backend spans into the existing timeline DTO shape so the UI can keep using the current Trace Timeline component boundary. | `AgctorSDK.Host/Controllers/VisualizationController.cs` and `AgctorSDK.Host/Models/ApiModels.cs` |
| 3.4 | Define clear outcomes for trace-not-found, backend-unavailable, and partially available timelines. | Trace query service plus controller response rules |

## Phase 4: Session transcript and visualization APIs

| Step | Action | Location |
| --- | --- | --- |
| 4.1 | Extend session transcript responses so each turn and message can advertise trace metadata and stable selection ids. | `AgctorSDK.Host/Controllers/ChatSessionsController.cs` |
| 4.2 | Add optional targeted endpoints to load a trace by session turn id or message id when transcript metadata alone is not enough. | `AgctorSDK.Host/Controllers/ChatSessionsController.cs` or `VisualizationController.cs` |
| 4.3 | Keep direct `trace/{traceId}/timeline` support for live loads and debugging. | `AgctorSDK.Host/Controllers/VisualizationController.cs` |
| 4.4 | Update endpoint documentation and diagrams after routes and DTOs are finalized. | `AgctorSDK.Host/docs/endpoints-diagram.*` and related docs |

## Phase 5: Dashboard chat and Trace Timeline UX

| Step | Action | Location |
| --- | --- | --- |
| 5.1 | Update chat transcript rendering so each historical turn exposes a default turn-level trace affordance. | `AgctorSDK.Host/wwwroot/js/dashboard/codegraph-page.js` |
| 5.2 | Add optional request/response drill-down only when message-level trace metadata differs from the turn-level trace. | `AgctorSDK.Host/wwwroot/js/dashboard/codegraph-page.js` and `AgentChat` markup |
| 5.3 | Highlight the currently selected turn or message while its timeline is shown. | Dashboard chat JS and Razor markup |
| 5.4 | Add loading, no-trace, not-found, and backend-unavailable states to the Trace Timeline experience. | `AgctorSDK.Host/Pages/Shared/Components/TraceTimeline/Default.cshtml` and dashboard JS |
| 5.5 | Preserve current behavior where the latest live response auto-loads its timeline immediately when available. | `AgctorSDK.Host/wwwroot/js/dashboard/codegraph-page.js` |

## Phase 6: Quality gate

| Step | Action | Location |
| --- | --- | --- |
| 6.1 | Add unit tests for trace correlation rules, transcript DTO mapping, and trace timeline mapping. | `AgctorSDK.Core.Tests` |
| 6.2 | Add integration tests for live prompt trace capture, historical transcript reload, and timeline retrieval after restart. | `AgctorSDK.Host.IntegrationTests` |
| 6.3 | Add regression coverage for sessions created before trace correlation exists. | `AgctorSDK.Host.IntegrationTests` |
| 6.4 | Build all affected projects, then run unit tests, then run integration tests. | Solution-level verification |

## Dependency Order

- Phase 1 must happen first because all later work depends on stable correlation models and DTO shape.
- Phase 2 and Phase 3 can proceed in parallel once contracts are agreed.
- Phase 4 depends on Phase 1 and should align with whichever correlation and trace query services are chosen.
- Phase 5 depends on Phase 4 because the UI needs stable trace-aware transcript data.
- Phase 6 should be updated continuously, with final build and test verification at the end.

## Suggested Implementation Order

1. Finalize correlation contracts and transcript DTO shape.
2. Implement actor-owned prompt-to-trace indexing.
3. Implement OpenTelemetry trace retrieval.
4. Wire transcript and visualization APIs.
5. Add dashboard trace history interactions.
6. Finish docs, builds, unit tests, and integration tests.

## Documentation Updates

- Update `AgctorSDK.Host/docs/architecture-diagram.*`
- Update `AgctorSDK.Host/docs/class-diagram.*`
- Update `AgctorSDK.Host/docs/endpoints-diagram.*`
- Update `AgctorSDK.Host/docs/dependencies-diagram.*`

## Notes

- Keep trace payload storage in the OpenTelemetry backend.
- Keep prompt-to-trace correlation durable inside AGCTOR-owned session infrastructure.
- Prefer turn-level trace selection by default, then layer in message-level drill-down only where it adds real debugging value.
