# PRD-021: People companion — Phase 3 automation

## 1. Overview

The people-project companion should learn from conversations without a separate “capture” panel. Phase 3 adds **automatic ingest on session end** and **actor-bound proactive reminders**, building on `PersonLifeSignalsReader` and the existing ingest pipeline.

## 2. Goals

| ID | Goal |
| --- | --- |
| G1 | When a project-bound chat session is **checkpointed** or **deleted**, new user/assistant turns since the last ingest cursor are sent through **ingest-only** ProjectMemory and written to scenario people markdown. |
| G2 | Persist chat `SessionSummary` with `LastIncludedSequence` so repeated checkpoints do not re-ingest the same turns. |
| G3 | Expose life-signal scans through a **`ProactiveSignalsActor`**; Host API uses the actor facade, not direct static calls. |
| G4 | Best-effort, non-blocking: failures log at debug and never block session delete/checkpoint or PRD-018 resolution emit. |

## 3. Non-goals

- Calendar/contacts import, confirmation inbox UX (Phase 4).
- Replacing PRD-018 reconciler `SessionSummary` messages.
- New playground capture controls.
- LLM-generated chat summaries (only pipeline `FinalText` snippet + sequence cursor).

## 4. Requirements

| ID | Requirement |
| --- | --- |
| R1 | `SessionEndIngestActor` accepts `SessionEndIngestWorkflowRequest` (sessionId, projectRoot, optional scenarioId, trigger). |
| R2 | Resolve `scenarioId` from `SessionProject` when the session has `projectId`. Skip ingest when there is no project or no new turns. |
| R3 | Wrap transcript with a session-end ingest preamble (same intent as `capture-to-memory`). |
| R4 | `ChatSessionsController` checkpoint + delete call `ISessionEndIngestService` before PRD-018 resolution emit. |
| R5 | `ProactiveSignalsActor` returns the same `PersonLifeSignal` list as `PersonLifeSignalsReader.Scan`. |
| R6 | Unit tests cover actor happy path, skip-when-no-new-turns, and proactive signals delegation. |

## 5. Acceptance criteria

1. Checkpointing a session with new turns runs ingest once; a second checkpoint with no new turns skips ingest.
2. `GET /api/project-memory/life-signals` succeeds and returns signals via the actor facade.
3. Playground Reminders panel behavior unchanged (still loads life-signals for active project scenario).
4. `dotnet build` and Core unit tests pass.
