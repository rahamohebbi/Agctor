# PRD-014: Implementation plan — Scenario Visual Designer Modal

**Status:** Planning baseline for v1 modal delivery.

## Phase 1: PRD + contracts

| Step | Action | Location |
| --- | --- | --- |
| 1.1 | Add `Project/prd-014/` readme + PRD + this plan | `Project/prd-014/` |
| 1.2 | Extend scenario contracts with optional `flow` model (nodes/edges/policies) | `AgctorSDK.Host/Services/Scenarios/ScenarioDefinitions.cs` |
| 1.3 | Extend API DTOs for `ScenarioDto.flow` (GraphDocument shape) | `AgctorSDK.Host/Models/ApiModels.cs` |
| 1.4 | GraphDocument JSON Schema in repo; copy to Host when validating | [`Project/prd-014/scenario-flow-schema.json`](./scenario-flow-schema.json), optional `AgctorSDK.Host/wwwroot/schemas/scenario-flow.schema.json` |

## Phase 2: catalog persistence + validation

| Step | Action | Location |
| --- | --- | --- |
| 2.1 | Normalize flow payload (ids/types/conditions) in catalog load/save path | `AgctorSDK.Host/Services/Scenarios/JsonScenarioCatalog.cs` |
| 2.2 | Validate graph integrity (entry/output, edges, reachability, merge policy) | `AgctorSDK.Host/Services/Scenarios/JsonScenarioCatalog.cs` |
| 2.3 | Validate `LlmNode` ids against project-memory registry and scenario persona roster | `AgctorSDK.Host/Services/Scenarios/JsonScenarioCatalog.cs` |

## Phase 3: API mapping and compatibility

| Step | Action | Location |
| --- | --- | --- |
| 3.1 | Map new flow fields in scenario controller list/get/put mappers | `AgctorSDK.Host/Controllers/ScenariosController.cs` |
| 3.2 | Keep existing fields (`agentTypes`, `personaAgentIds`, `personaBindings`) backward compatible | `AgctorSDK.Host/Controllers/ScenariosController.cs` |
| 3.3 | Add structured validation error payload for flow failures | `AgctorSDK.Host/Controllers/ScenariosController.cs` |

## Phase 4: modal UI on Scenarios page

| Step | Action | Location |
| --- | --- | --- |
| 4.1 | Add “Open visual designer” trigger and modal shell (container `div` for graph + palette slots) | `AgctorSDK.Host/Pages/Dashboard/Scenarios.cshtml` |
| 4.2 | Add **GraphRenderer** facade module (interface only in JSDoc/comments); no Cytoscape imports outside adapter | `AgctorSDK.Host/wwwroot/js/dashboard/scenario-flow/graph-renderer.js` |
| 4.3 | Implement **CytoscapeAdapter**: `GraphDocument` ↔ Cytoscape `elements` + style; persist positions into `flow.ui.nodeLayouts` on read | `AgctorSDK.Host/wwwroot/js/dashboard/scenario-flow/cytoscape-adapter.js` |
| 4.4 | Pure **graph-document** helpers: validate structure, simulate path (no DOM) | `AgctorSDK.Host/wwwroot/js/dashboard/scenario-flow/graph-document.js` |
| 4.5 | Wire modal open/close: `mount` / `destroy` / `read`; palette + property panel from `scenarios-page.js` | `AgctorSDK.Host/wwwroot/js/dashboard/scenarios-page.js` |
| 4.6 | Load Cytoscape.js as static script (vendored under `wwwroot/lib/cytoscape/` or version-pinned CDN in layout section Scripts) | `AgctorSDK.Host/Pages/Dashboard/Scenarios.cshtml` or `_Layout.cshtml` |
| 4.7 | Implement Validate/Simulate/Save wiring in modal footer | `AgctorSDK.Host/wwwroot/js/dashboard/scenarios-page.js` |
| 4.8 | Add persistent helper copy clarifying runtime vs non-runtime semantics | `AgctorSDK.Host/Pages/Dashboard/Scenarios.cshtml` |

## Phase 4b: versioning UI (optional slice after v1 canvas)

| Step | Action | Location |
| --- | --- | --- |
| 4b.1 | List revisions / archived flows for scenario; load snapshot into modal | API + `scenarios-page.js` |
| 4b.2 | Soft-delete flow or archive revision | `JsonScenarioCatalog` + DTO `status` |

## Phase 5: quality + docs

| Step | Action | Location |
| --- | --- | --- |
| 5.1 | Unit tests for flow normalization and graph validation (C#) | `AgctorSDK.Host.Tests` or existing scenario-focused test project |
| 5.2 | Unit or browser-less tests for **GraphDocument round-trip** via adapter (if extracted to shared test assets, or manual QA checklist v1) | Document in PRD until automated |
| 5.3 | Integration tests for `GET/PUT /api/scenarios` with flow payload round-trip | `AgctorSDK.Host.IntegrationTests/` |
| 5.4 | UI smoke tests for modal open/save and validation feedback | `AgctorSDK.Host.IntegrationTests/` |
| 5.5 | Update Host endpoint/class diagrams if API shapes change | `AgctorSDK.Host/docs/` |

## Phase 6: runtime contract + shared persona LLM

| Step | Action | Location |
| --- | --- | --- |
| 6.1 | Extract playground-equivalent **prompt build + Ollama generate** behind `IProjectMemoryPersonaLlmRunner` | `AgctorSDK.Host/Services/ProjectMemory/` |
| 6.2 | Refactor `POST …/project-memory/playground/run` to call the runner (DRY) | `AgctorSDK.Host/Controllers/ProjectMemoryController.cs` |
| 6.3 | Document run context: upstream text per node; Router deterministic rules (Phase 10 adds LLM router) | `Project/prd-014/prd-014-scenario-visual-designer-modal.md` |

## Phase 7: flow graph interpreter + HTTP API

| Step | Action | Location |
| --- | --- | --- |
| 7.1 | Implement `ScenarioFlowGraphInterpreter` (sequential edges; Router; LlmNode; Merge; Output; cycle guard) | `AgctorSDK.Host/Services/Scenarios/` |
| 7.2 | Implement `IScenarioFlowExecutionService` (catalog + validation + project root gate) | `AgctorSDK.Host/Services/Scenarios/` |
| 7.3 | `POST /api/scenarios/{id}/flow/run` | `AgctorSDK.Host/Controllers/ScenariosController.cs` + `ApiModels` |
| 7.4 | Register services in DI | `AgctorSDK.Host/Program.cs` |
| 7.5 | Tests: interpreter with stub invoker; integration test for `flow/run` 4xx paths | `AgctorSDK.Host.IntegrationTests/` |

## Phase 8: parallel edges + chat integration ✅ (delivered)

| Step | Action | Location |
| --- | --- | --- |
| 8.1 | Execute `parallel` fan-out / join with per–LlmNode timeout and deterministic Merge (no nested parallel fork inside a branch) | `ScenarioFlowGraphInterpreter.cs`, `ScenarioFlowExecutionService.cs`, PRD |
| 8.2 | Wire runner into dashboard chat: `session-coordinator-agent` + current scenario with `flow` → `IScenarioFlowExecutionService` before actor dispatch | `MessageDispatcher.cs` |

## Phase 9: observability

| Step | Action | Location |
| --- | --- | --- |
| 9.1 | Structured logs per node execution (scenario id, node id, persona id, elapsed ms) | Runner + `ILogger` |

## Phase 10: Smart LLM Router ✅ (delivered)

| Step | Action | Location |
| --- | --- | --- |
| 10.1 | Freeze `Router.config` keys (`routerMode`, `maxTargets`, fallback, thresholds) + migration from deterministic-only | `prd-014-scenario-visual-designer-modal.md`, `scenario-flow.schema.json` |
| 10.2 | Keep [`scenario-flow-router-response.schema.json`](./scenario-flow-router-response.schema.json) as contract; optional copy under `AgctorSDK.Host/wwwroot/schemas/` | `Project/prd-014/`, Host `wwwroot/schemas/` |
| 10.3 | `IScenarioFlowRouterLlmService` + Ollama prompt with candidate blurbs → parse JSON → whitelist `personaId` set | `AgctorSDK.Host/Services/Scenarios/`, `OllamaGenerateApi.cs` |
| 10.4 | `ScenarioFlowGraphInterpreter`: discover Router → LlmNode edges; on `routerMode: llm`, call routing LLM; run selected LlmNodes (**default:** parallel + existing `Merge`) | `ScenarioFlowGraphInterpreter.cs` |
| 10.5 | Catalog validation: LLM Router structure (sequential LlmNode edges, shared Merge, fallback rules) | `ScenarioFlowValidator.cs`, `JsonScenarioCatalog.cs` |
| 10.6 | Modal: Router property panel — mode, read-only candidate list; Simulate note (no live routing LLM) | `Scenarios.cshtml`, `scenarios-page.js` |
| 10.7 | Tests: mock routing JSON; multi-target + Merge; parser + interpreter coverage | `AgctorSDK.Host.IntegrationTests/` |

## Phase 11: LlmNode persona picker (modal UX) ✅ (delivered)

**Goal:** When adding or editing a **`LlmNode`** node, the operator chooses a **known persona** from the scenario roster using **human-readable labels** (e.g. “Memory Curator”, “Person Extractor”), while the graph continues to persist **`config.personaId`** (YAML `id`, e.g. `memory-curator`, `person-extractor`).

| Step | Action | Location |
| --- | --- | --- |
| 11.1 | **LlmNode inspector**: show when exactly one selected node is `LlmNode` — dropdown of **allowed ids** = current scenario `personaAgentIds` (same roster as server validation) | `Scenarios.cshtml`, `scenarios-page.js` (`agctorConfig` via existing adapter) |
| 11.2 | **Display names**: map `personaId` → label using **`GET /api/project-memory/agents`** (`name`, fallback `role`); cache per modal open | `ProjectMemoryController` (existing); `scenarios-page.js` |
| 11.3 | **Add-node default**: pre-select first roster id; empty roster → amber message + empty `personaId` | `scenarios-page.js` |
| 11.4 | **Client validate**: `validateFlowDocument(doc, { personaAgentIds })` — roster membership + empty roster when any `LlmNode` | `graph-document.js`, modal Validate button |
| 11.5 | **Tests**: `GET /api/project-memory/agents` returns YAML names for default sample project | `AgctorSDK.Host.IntegrationTests/ProjectMemoryAgentsListIntegrationTests.cs` |

## Dependency order

1 → 2 → 3 → 4 → 5 → **6 → 7**; **4b** (revisions UI) after 4 when canvas save is stable; **8–9** after 7 as needed; **10** after 8 (reuses parallel + Merge + persona runner patterns); **11** after 4 (modal + `personaAgentIds`); **11.2** may depend on stable project-memory list API.

## Actor-model alignment checklist

- Flow **runner** does not let clients spawn actors; it calls **approved services** (persona LLM runner today).
- Scenario **apply** remains the path for coordinator/session actor bootstrap.
- **LlmNode** resolves YAML specs from disk (project memory); execution is server-side only.
- **Router:** **Shipped** = deterministic (substring + default edge) **and** optional **`routerMode: llm`** (structured JSON, whitelist-only `personaId`s from graph-discovered candidates). **Phase 11** = richer **`LlmNode`** picker in the modal (roster + YAML display names).
- **Parallel** edges: one fan-out per hop, single shared `Merge`, no nested parallel inside a branch; per–`LlmNode` timeout enforced.

## Portability checklist (renderer swap)

- Canonical persistence is **GraphDocument** only; adapter is the only module that imports Cytoscape.
- Simulation and server validation operate on **GraphDocument**, not on Cytoscape instances.
- Layout and theme are under `flow.ui` or adapter-local defaults; swapping renderer rewrites adapter only.

## Test strategy

- Build all projects.
- Run unit tests first.
- Run integration tests second (scenario flow and modal-related API coverage).
- Keep existing scenario apply tests green.

