# PRD-020 — Actor-first maintainability and runtime consistency

**Folder status:** Planned — architecture hardening PRD for making AGCTOR easier to maintain as an actor-model and agent framework.

This PRD turns the architecture scan findings into a focused improvement plan. It does not add a new product surface first; it strengthens the internal system boundaries that future features depend on.

## Documents

| File | Purpose |
| --- | --- |
| [prd-020-agctor-prd.md](./prd-020-agctor-prd.md) | Goals, current risks, requirements, acceptance criteria |
| [prd-020-implementation-plan.md](./prd-020-implementation-plan.md) | Phased delivery, module placement, tests, risks |
| [prd-020-tracking.md](./prd-020-tracking.md) | Live phase status, requirement map, decision log, risk log, and verification gates |

## Why this matters

AGCTOR is an agentic framework built around the Actor Model. The source code is strongest when important workflows are expressed as messages between isolated actors, when message contracts are shared instead of stringly typed, and when runtime adapters behave the same way from the application’s point of view.

The scan found three high-priority maintainability risks:

1. **ProjectMemory’s main ingest/query path is service-first, not actor-first.**
2. **Actor messages rely too much on loose string headers and raw payload shapes.**
3. **Runtime adapters duplicate envelope/correlation behavior and may drift.**

## Relationship to other PRDs

- **PRD-016** — ProjectMemory ingest and playground trace behavior remain the functional baseline. PRD-020 should preserve existing file effects while changing orchestration boundaries.
- **PRD-018** — Entity resolution is the best current example of actor-owned state. PRD-020 should reuse that style as the reference pattern.
- **PRD-019** — Out-of-schema generic inbox work is referenced in the current branch. PRD-020 should not change its product behavior, but should route future confirmation/persistence flows through the same actor/message conventions where practical.

## Core principle

Make the Actor Model the default architecture for AGCTOR workflows:

1. A workflow step that owns state or side effects should have an actor boundary.
2. Actors should exchange typed messages or shared envelope helpers, not hand-built header dictionaries.
3. Runtime adapters should pass the same behavioral contract tests.
4. Agent YAML, C# agents, and Host persona runners should share one source of prompt and LLM behavior wherever possible.

## Key baseline code locations

| Area | Location |
| --- | --- |
| Actor contract | `AgctorSDK.Core/Interfaces/IActor.cs` |
| Runtime adapter contract | `AgctorSDK.Core/Interfaces/IActorRuntimeAdapter.cs` |
| Message envelope | `AgctorSDK.Core/Messages/MessageEnvelope.cs` |
| In-memory runtime | `AgctorSDK.Agents/Adapters/InMemoryActorRuntime.cs` |
| Proto runtime | `AgctorSDK.Agents/Adapters/ProtoActorAdapter.cs` |
| Agent base behavior | `AgctorSDK.Agents/Agents/Agent.cs` |
| Agent factory | `AgctorSDK.Agents/Agents/AgentFactory.cs` |
| ProjectMemory pipeline | `AgctorSDK.Core/ProjectMemory/Orchestration/ProjectMemoryPipelineRunner.cs` |
| ProjectMemory actor workflow | `AgctorSDK.Core/ProjectMemory/Orchestration/Actors/` |
| ProjectMemory actors reference | `AgctorSDK.Core/ProjectMemory/Resolution/Actors/` |
| Project agent YAML model | `AgctorSDK.Core/ProjectMemory/Models/AgentDefinitionSpec.cs` |
| Host persona runner | `AgctorSDK.Host/Services/ProjectMemory/ProjectMemoryPersonaLlmRunner.cs` |

## Migration note

New ProjectMemory orchestration work should target the actor workflow entry points under `AgctorSDK.Core/ProjectMemory/Orchestration/Actors/`. The existing `ProjectMemoryPipelineRunner` remains the compatibility path until actor parity is fully proven and the default execution mode can be flipped safely.

