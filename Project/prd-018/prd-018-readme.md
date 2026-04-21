# PRD-018 — Cross-session entity resolution (soft → hard links with evidence)

**Folder status:** Active — specification and phased plan for **actor-owned entity resolution** across sessions in AGCTOR, with graded linkage (soft / hard), weighted evidence, and auditable promotions.

> Originally discussed as “PRD-017”; filed here as PRD-018 because PRD-017 is already allocated to the Playground hierarchy work.

## Documents

| File | Purpose |
| --- | --- |
| [prd-018-agctor-prd.md](./prd-018-agctor-prd.md) | Goals, actor topology, message protocol, evidence schema, acceptance criteria |
| [prd-018-implementation-plan.md](./prd-018-implementation-plan.md) | Phased delivery, modules, risks, tests |
| [prd-018-agent-configuration-and-flow.md](./prd-018-agent-configuration-and-flow.md) | How to configure agents for PRD-018 and how scenario flow should (or should not) change |
| [docs/resolution-actor-topology.mmd](./docs/resolution-actor-topology.mmd) | Actor diagram (supervisor, reconciler, per-entity resolution, mention index) |
| [docs/resolution-state-machine.mmd](./docs/resolution-state-machine.mmd) | Edge state machine: soft / hard / rejected / superseded |
| [docs/resolution-message-flow.mmd](./docs/resolution-message-flow.mmd) | Cross-session sequence flow (Raha/Ryan example) |

## Relationship to other PRDs

- **PRD-016** — Ingest remains the only writer of markdown; PRD-018 emits `IngestIntentDraft` messages that PRD-016’s pipeline materializes, and re-uses the **Input / Outcome** trace-drill convention for a new `pm.playground.resolve` span.
- **PRD-013 / PRD-014** — Scenario catalog and flows are unchanged; resolution runs alongside `person-extractor` in the same flow.
- **PRD-009** — Trace-timeline UX; PRD-018 adds one span kind that follows the same structure.
- **Future PRDs** — Narrative/content merging for hard-linked entities is out of scope here and left to a follow-up (see PRD-018 §8 open question 2).

## Core principle

Lean on the actor model wherever it adds value:

1. **Per-entity actor** owns that entity’s inbound evidence and is the only writer of its `.resolution/` sidecar.
2. **Reconciler actor** batches and coalesces candidate work across sessions.
3. **Mention-index actor** projects surface forms → candidate entities, rebuilt on startup from the registry.
4. **Supervisor actor** rehydrates children from disk on fault; evidence is append-only on purpose.
5. **Location-transparent** across `InMemory`, Orleans, or Proto.Actor adapters.

## Key code locations (baseline before work)

| Area | Location |
| --- | --- |
| Entity discovery | `AgctorSDK.Core/ProjectMemory/Loading/EntityRegistry.cs` |
| Entity metadata model | `AgctorSDK.Core/ProjectMemory/Models/EntityMetadata.cs` |
| Memory-intent JSON contract | `AgctorSDK.Core/ProjectMemory/Orchestration/MemoryIntentJson.cs` |
| Ingest orchestration | `AgctorSDK.Core/ProjectMemory/Orchestration/ProjectMemoryPipelineRunner.cs` |
| Actor runtime adapter | `AgctorSDK.Core/Interfaces/IActorRuntimeAdapter.cs` |
| Supervisor pattern reference | `AgctorSDK.Core/Actors/TimeoutSupervisorActor.cs` |
| Trace payload JSON (playground) | `AgctorSDK.Host/Services/ProjectMemory/PlaygroundTraceTimelineDetail.cs` |
| Trace timeline UI | `AgctorSDK.Host/Pages/Shared/Components/TraceTimeline/Default.cshtml` |
| Playground SSE + ingest gate | `AgctorSDK.Host/Controllers/ProjectMemoryController.cs` |
| Sample person-extractor agent | `samples/people-project/.agctor/agents/people/person-extractor.agent.yaml` |
| Sample entity (demonstrates aliases) | `samples/people-project/people/raha/entity.yaml` |

## Proposed module placement

| New module | Project |
| --- | --- |
| Resolution actors, messages, policy, evidence store | `AgctorSDK.Core/ProjectMemory/Resolution/` |
| Signal producers (alias, uniqueness, coref, attr, embedding, graph, negative) | `AgctorSDK.Core/ProjectMemory/Resolution/Signals/` |
| Ingest intent bridge | `AgctorSDK.Core/ProjectMemory/Resolution/Bridge/` |
| Unit tests | `AgctorSDK.Core.Tests/ProjectMemory/Resolution/` |
| Integration tests (reconciler end-to-end) | `AgctorSDK.Core.IntegrationTests/ProjectMemory/Resolution/` |
| Trace span DTO + UI adapter | `AgctorSDK.Host/Services/ProjectMemory/`, `AgctorSDK.Host/Pages/Shared/Components/TraceTimeline/` |
| Review tab | `AgctorSDK.Host/Pages/Dashboard/ProjectMemory/ResolutionReview.cshtml` |
