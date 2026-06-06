# PRD-024 — Scenario Flow Loops & Agctor Scenario Flow Studio

**Folder status:** Active — specification and implementation plan ready for delivery.

## Summary

**PRD-024** adds **multi-turn, suspend/resume, and loop-back** execution to scenario flows (schema **2.0**), backed by a session-scoped **`ScenarioFlowRuntimeActor`**. The dashboard tool is renamed to **Agctor Scenario Flow Studio** and gains first-class support for new node types (`Gate`, `Ask user`, `Wait for event`, `Notify`) and **`loopBack`** edges.

**Reference scenario:** People **style coach** loop — no photos → ask user → ingest/extract → notify → re-run style advice.

## Documents

| File | Purpose |
| --- | --- |
| [prd-024-agctor-prd.md](./prd-024-agctor-prd.md) | Goals, actor model, runtime protocol, node/edge semantics, APIs, acceptance criteria |
| [prd-024-ux-spec.md](./prd-024-ux-spec.md) | Scenario Flow Studio: palette, inspectors, loop edges, multi-turn simulate |
| [prd-024-implementation-plan.md](./prd-024-implementation-plan.md) | Phased delivery, file map, tests |
| [scenario-flow-schema-v2.json](./scenario-flow-schema-v2.json) | GraphDocument **2.0** JSON Schema (extends PRD-014 v1) |
| [scenario-flow-runtime-snapshot.schema.json](./scenario-flow-runtime-snapshot.schema.json) | Persisted runtime snapshot (`executionNodeId`, loop regions, store) |
| [docs/style-photo-loop-flow.mmd](./docs/style-photo-loop-flow.mmd) | Reference flow diagram (style + photos) |
| [docs/flow-runtime-state-machine.mmd](./docs/flow-runtime-state-machine.mmd) | Runtime actor state machine |

## Relationship to other PRDs

| PRD | Relationship |
| --- | --- |
| **PRD-014** | v1 graph designer, interpreter, `flow/run` — **extended**, not replaced |
| **PRD-013** | Agent Studio / scenario catalog — Studio rename aligns with dashboard surfaces |
| **PRD-023** | Visual ingest/extract actors and `people` flow — **reference loop** consumer |
| **PRD-019** | Generic inbox — in-graph confirm via `Gate` + `Ask user` loop |
| **PRD-020** | Actor/tool patterns — runtime actor + domain event messages |
| **PRD-022** | Chat inbox approve/reject — wired as `inbox.confirmed` domain event |

## Locked product decisions (from design review)

| Topic | Decision |
| --- | --- |
| Runtime scope | One `ScenarioFlowRuntimeActor` per **applied scenario on session** |
| Photo re-entry | **Delta** ingest for new attachments; re-enter same subgraph entry node |
| Style coach refresh | **`loopBack`** to style-coach for user-facing answer; optional `Notify` for background |
| Generic inbox | **In-graph** `Gate` + `Ask user` loop (not playground-only side channel) |
| Execution position field | **`executionNodeId`** (UI: **At node:** …) — not `cursor` |

## Key code locations (planned)

| Area | Location |
| --- | --- |
| Runtime actor + messages | `AgctorSDK.Core/ProjectMemory/Scenarios/Actors/` (new) |
| Flow runtime service + snapshot store | `AgctorSDK.Host/Services/Scenarios/` |
| v2 interpreter / runtime orchestration | `ScenarioFlowRuntimeService.cs`, evolves `ScenarioFlowGraphInterpreter.cs` |
| Message dispatch integration | `AgctorSDK.Host/Services/MessageDispatcher.cs` |
| Visual domain events | `AgctorSDK.Core/ProjectMemory/Visual/Actors/` |
| Studio UI | `AgctorSDK.Host/Pages/Dashboard/Scenarios.cshtml`, `wwwroot/js/dashboard/scenario-flow/`, `scenarios-page.js` |
| JSON Schema (Host copy) | `AgctorSDK.Host/wwwroot/schemas/scenario-flow.schema.json` (v2) |
| Integration tests | `AgctorSDK.Host.IntegrationTests/ScenarioFlowLoop*.cs` |

## Naming

| Before | After |
| --- | --- |
| Scenario flow designer | **Agctor Scenario Flow Studio** |
| Scenario Visual Designer Modal (PRD-014) | Superseded in UX copy by **Scenario Flow Studio** |
| `cursor` / `cursorNodeId` | **`executionNodeId`** |
