# PRD-021 — Implementation plan

## Phase 1 — Core (this delivery)

1. `SessionTranscriptFormatter` — shared User/Assistant transcript lines (used by Host capture API and session-end actor).
2. `CompanionMemoryMessages` — `SessionEndIngestWorkflowRequest/Result`, `ProactiveSignalsWorkflowRequest/Result`.
3. `SessionEndIngestActor` — `ISessionStore` + `IProjectMemoryPipelineRunner`, incremental cursor via `SessionSummary.LastIncludedSequence`.
4. `ProactiveSignalsActor` — delegates to `PersonLifeSignalsReader.Scan`.
5. `ActorBackedCompanionMemoryServices` — spawns actors via `IActorRuntimeAdapter`, implements `ISessionEndIngestService` + `IProactiveSignalsService`.
6. Register services in `HostServiceCollectionExtensions.AddAgctorHost`.

## Phase 2 — Host wiring

1. `ChatSessionsController` — inject `ISessionEndIngestService` + `IOptions<ProjectMemoryAgentOptions>`; call on checkpoint/delete (best-effort).
2. `ProjectMemoryController.GetLifeSignals` — use `IProactiveSignalsService`.
3. `ProjectMemoryController` — use `SessionTranscriptFormatter` in capture path (remove duplicate private helper).

## Tests

| Test | Location |
| --- | --- |
| Session end ingest skips / runs | `AgctorSDK.Core.Tests/ProjectMemory/SessionEndIngestActorTests.cs` |
| Proactive signals actor | `AgctorSDK.Core.Tests/ProjectMemory/ProactiveSignalsActorTests.cs` |
| Life-signals HTTP (optional) | `AgctorSDK.Host.IntegrationTests` if host test harness exists |

## Risks

| Risk | Mitigation |
| --- | --- |
| Duplicate timeline rows on re-ingest | `LastIncludedSequence` cursor |
| LLM ingest cost on every delete | Skip when no new turns; ingest-only mode |
| Actor runtime not configured | Facade requires `IActorRuntimeAdapter` (same as PRD-020 pipeline) |
