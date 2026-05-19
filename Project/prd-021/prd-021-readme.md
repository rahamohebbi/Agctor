# PRD-021 — Companion Phase 3 (session ingest + proactive signals)

**Status:** In progress — closes the people-companion **Phase 3** gap: automatic session → memory ingest and actor-wrapped life-signal nudges.

## Documents

| File | Purpose |
| --- | --- |
| [prd-021-agctor-prd.md](./prd-021-agctor-prd.md) | Goals, requirements, acceptance criteria |
| [prd-021-implementation-plan.md](./prd-021-implementation-plan.md) | Module placement, hooks, tests |

## Scope (short)

1. **Session end ingest** — On chat session **checkpoint** or **delete**, replay **new** transcript turns through the existing ProjectMemory **ingest-only** pipeline and persist a rolling `SessionSummary` cursor (`LastIncludedSequence`).
2. **Proactive signals actor** — `GET /api/project-memory/life-signals` routes through an actor that delegates to `PersonLifeSignalsReader` (playground Reminders strip unchanged).
3. **UX** — Single playground composer + scenario router only (no second capture UI); `quick-capture` / `capture-to-memory` APIs remain for automation.

## Related PRDs

- **PRD-016/018** — ProjectMemory pipeline and resolution `SessionSummary` (reconciler) are separate from chat `SessionSummary` ingest cursor.
- **PRD-020** — Reuses actor runtime + typed workflow messages pattern.

## Key code (after implementation)

| Area | Location |
| --- | --- |
| Session transcript helper | `AgctorSDK.Core/Sessions/SessionTranscriptFormatter.cs` |
| Companion actors | `AgctorSDK.Core/ProjectMemory/Companion/Actors/` |
| Actor facade | `AgctorSDK.Core/ProjectMemory/Companion/ActorBackedCompanionMemoryServices.cs` |
| Session hooks | `AgctorSDK.Host/Controllers/ChatSessionsController.cs` |
| Life signals API | `AgctorSDK.Host/Controllers/ProjectMemoryController.cs` |
