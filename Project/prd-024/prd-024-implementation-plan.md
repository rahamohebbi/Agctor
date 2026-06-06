# PRD-024: Implementation plan — Scenario Flow Loops & Scenario Flow Studio

**Status:** Planning baseline for v2 loop delivery.

**Prerequisite:** PRD-014 Phases 1–11 delivered (flow designer, interpreter, LLM router).

---

## Phase A — Contracts & runtime foundation

| Step | Action | Location |
| --- | --- | --- |
| A.1 | Add `Project/prd-024/` readme, PRD, UX spec, schemas | `Project/prd-024/` |
| A.2 | Copy `scenario-flow-schema-v2.json` → Host `wwwroot/schemas/` (merge or side-by-side with v1) | `AgctorSDK.Host/wwwroot/schemas/` |
| A.3 | Define `ScenarioFlowRuntimeSnapshot` model + file store interface | `AgctorSDK.Core/ProjectMemory/Scenarios/` |
| A.4 | Define actor messages: `StartFlow`, `ResumeWithUserInput`, `ResumeWithDomainEvent`, `CancelFlow` | `AgctorSDK.Core/ProjectMemory/Scenarios/Messages/` |
| A.5 | Implement `ScenarioFlowRuntimeActor` (state machine per [flow-runtime-state-machine.mmd](./docs/flow-runtime-state-machine.mmd)) | `AgctorSDK.Core/ProjectMemory/Scenarios/Actors/` |
| A.6 | Register actor factory + spawn on scenario apply / first message | `AgctorSDK.Host/DependencyInjection/`, `ScenarioApplicationService.cs` |
| A.7 | Unit tests: snapshot transitions, attempt limits, `executionNodeId` updates | `AgctorSDK.Core.Tests/ProjectMemory/ScenarioFlowRuntimeTests.cs` |

---

## Phase B — Segment runner & v2 node execution

| Step | Action | Location |
| --- | --- | --- |
| B.1 | Refactor interpreter into **segment runner**: start at `executionNodeId`, stop at suspend/Output | `ScenarioFlowGraphInterpreter.cs` → `ScenarioFlowSegmentRunner.cs` |
| B.2 | Implement `Gate` evaluation against runtime fact store | `ScenarioFlowGateEvaluator.cs` |
| B.3 | Implement `WaitForInput` suspend (no Output) | Segment runner |
| B.4 | Implement `AwaitEvent` suspend + timeout scheduling | Segment runner + runtime actor timer |
| B.5 | Implement `Notify` (signal dispatch only) | `ScenarioFlowNotifyDispatcher.cs` |
| B.6 | Implement `loopBack` traversal: invalidation, attempt increment, `executionNodeId` jump | Segment runner + runtime |
| B.7 | Extend `ScenarioFlowValidator` for v2 rules (regions, suspend paths, session cap) | `ScenarioFlowValidator.cs` |
| B.8 | Integration tests: two-turn WaitForInput; loopBack max attempts | `AgctorSDK.Host.IntegrationTests/ScenarioFlowLoopTests.cs` |

---

## Phase C — Domain events & chat integration

| Step | Action | Location |
| --- | --- | --- |
| C.1 | Publish `visual.extract.completed` / `failed` to runtime actor mailbox | `VisualExtractActor`, `ActorBackedVisualPipelineService` |
| C.2 | Publish `inbox.confirmed` on generic inbox approve | `GenericInboxDecisionService`, `ProjectMemoryGenericInboxActor` |
| C.3 | Wire `MessageDispatcher` → runtime actor for v2 flows (detect schemaVersion or node types) | `MessageDispatcher.cs` |
| C.4 | Extend `POST /api/scenarios/{id}/flow/run` response: `status`, `executionNodeId`, `pendingPrompt`, `completed` | `ScenariosController.cs`, `ApiModels.cs` |
| C.5 | Delta attachment tracking in snapshot on resume | Runtime store |
| C.6 | Integration test: style photo loop (reference diagram) | `ScenarioFlowStylePhotoLoopTests.cs` |
| C.7 | Migrate playground inbox confirm to graph path (feature flag `flow.inboxInGraph`) | `ProjectMemoryController.cs` |

---

## Phase D — Scenario Flow Studio (UI)

| Step | Action | Location |
| --- | --- | --- |
| D.1 | Rename UI copy to **Agctor Scenario Flow Studio** | `Scenarios.cshtml`, `scenarios-page.js` |
| D.2 | Add palette entries: Gate, Ask user, Wait for event, Notify | `Scenarios.cshtml`, `scenarios-page.js` |
| D.3 | Cytoscape node styles + shapes for v2 types | `cytoscape-adapter.js` |
| D.4 | Loop back edge mode: dashed styling, create/convert tools | `cytoscape-adapter.js`, `scenarios-page.js` |
| D.5 | Inspector panels for v2 nodes + `loopConfig` on edges | `scenarios-page.js` |
| D.6 | Extend `graph-document.js` validate + simulate turns | `graph-document.js` |
| D.7 | Loop region overlay toggle | `cytoscape-adapter.js` |
| D.8 | Execution summary panel (At node, status, attempts) | `scenarios-page.js` |
| D.9 | Client schema validation against v2 JSON Schema (optional AJV) | `graph-document.js` |

---

## Phase E — Catalog template & docs

| Step | Action | Location |
| --- | --- | --- |
| E.1 | Add `people-style-photo-loop` example flow to sample catalog | `agctor-scenarios.user.json`, `samples/people-project/` |
| E.2 | Update PRD-014 readme cross-link to PRD-024 | `Project/prd-014/prd-014-readme.md` |
| E.3 | Host docs: class diagram delta for runtime actor | `AgctorSDK.Host/docs/` |
| E.4 | Mermaid JPEG generation for PRD-024 docs diagrams (per workspace docs rule) | `Project/prd-024/docs/` |

---

## Phase F — Quality gate (required before merge)

Per workspace build rule:

1. `dotnet build` — full solution
2. Unit tests — `AgctorSDK.Core.Tests` (runtime + gate + validator)
3. Integration tests — `AgctorSDK.Host.IntegrationTests` (loop + style photo + v1 regression)
4. Integration tests — `AgctorSDK.Core.IntegrationTests` if snapshot touches memory actors

---

## File map (new / major touch)

| File | Purpose |
| --- | --- |
| `ScenarioFlowRuntimeActor.cs` | Session+scenario flow state owner |
| `ScenarioFlowRuntimeSnapshot.cs` | Persisted snapshot model |
| `ScenarioFlowRuntimeStore.cs` | File-backed snapshot persistence |
| `ScenarioFlowSegmentRunner.cs` | Execute graph segment from `executionNodeId` |
| `ScenarioFlowGateEvaluator.cs` | Gate fact evaluation |
| `ScenarioFlowLoopTraversal.cs` | loopBack + invalidation |
| `ScenarioFlowRuntimeService.cs` | Host facade: start/resume/cancel |
| `ScenarioFlowLoopTests.cs` | Integration coverage |

---

## Feature flags (recommended)

| Flag | Default | Purpose |
| --- | --- | --- |
| `Agctor:ScenarioFlow:RuntimeActorEnabled` | `true` when v2 graph | Kill switch |
| `Agctor:ScenarioFlow:InboxInGraph` | `false` until C.7 | Playground migration |

---

## Dependencies & ordering

```
Phase A ──► Phase B ──► Phase C
              │
              └──► Phase D (can start after B.1 for UI mocks)
Phase E ── after C.6
Phase F ── after each phase merge + final
```

---

## Out of scope (defer)

- Automatic dagre layout for loop-back edges
- Server-backed Studio simulate (stretch in UX spec)
- Cross-session snapshot restore
- Custom fact scripting language
