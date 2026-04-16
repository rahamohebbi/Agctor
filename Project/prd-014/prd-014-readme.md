# PRD-014 — Scenario Visual Designer Modal

**Folder status:** Active — specification and implementation plan ready for delivery.

## Documents

| File | Purpose |
| --- | --- |
| [prd-014-scenario-visual-designer-modal.md](./prd-014-scenario-visual-designer-modal.md) | Full PRD: goals, UX, canonical graph format, renderer abstraction, API, acceptance criteria |
| [prd-014-implementation-plan.md](./prd-014-implementation-plan.md) | Phased delivery plan with code locations and test strategy |
| [scenario-flow-schema.json](./scenario-flow-schema.json) | JSON Schema (Draft 2020-12) for `flow` / GraphDocument structure validation |
| [scenario-flow-router-response.schema.json](./scenario-flow-router-response.schema.json) | Phase 10 LLM **Router** step: validated JSON output contract (candidate `personaId`s only) |

## Relationship to other PRDs

- **PRD-013** introduced unified runtime vs non-runtime definition UX and scenario editing. **PRD-014** adds a visual orchestration modal on `/Dashboard/Scenarios` while preserving that runtime/non-runtime separation.
- **PRD-012** provides the same documentation packaging pattern (readme + full PRD + implementation plan); PRD-014 follows that structure.

## Implemented summary (target v1)

- **Runtime:** `POST /api/scenarios/{id}/flow/run` executes **sequential** and **parallel fan-out → Merge** graphs; `PersonaCall` invokes project-memory YAML + Ollama (default 180s timeout per call, overridable). Dashboard messages to `session-coordinator-agent` use the flow when the applied scenario defines `flow`.
- Modal launched from `/Dashboard/Scenarios` for visual flow design.
- Simple node-based orchestration for scenario request handling:
  - `ChatInput`
  - `Router`
  - `PersonaCall`
  - `Merge`
  - `Output`
- Saved visual flow remains canonical and compatible with current scenario model (`agentTypes`, `personaAgentIds`, `personaBindings`).
- **Portable graph format:** domain-owned **GraphDocument** JSON (not a renderer dump); optional **JSON Schema** for validation only.
- **Rendering:** [Cytoscape.js](https://js.cytoscape.org/) in **vanilla JS** (no React/Vue); thin **adapter** behind a `GraphRenderer`-style interface so the library can be swapped later.
- **Versioning / lifecycle:** revisions or Git-backed files; soft-delete (`archived` / `deleted`) before hard remove where appropriate.
- **Delivered (Phase 10):** **Smart LLM Router** — optional `routerMode: llm`; auto-discovers downstream `PersonaCall` candidates from graph edges; structured JSON ([`scenario-flow-router-response.schema.json`](./scenario-flow-router-response.schema.json)); multi-target default → parallel `PersonaCall` + `Merge`. See PRD section *Smart LLM Router*.
- **Delivered (Phase 11):** **PersonaCall persona picker** — flow modal: roster dropdown with **YAML display names** from `GET /api/project-memory/agents`, client validation vs `personaAgentIds`. See implementation plan Phase 11 and PRD section *PersonaCall property UX*.

## Key code locations (planned)

| Area | Location |
| --- | --- |
| Scenario page shell and modal host | `AgctorSDK.Host/Pages/Dashboard/Scenarios.cshtml` |
| Scenario page behavior + modal shell wiring | `AgctorSDK.Host/wwwroot/js/dashboard/scenarios-page.js` |
| Canonical graph ↔ renderer adapter (v1: Cytoscape) | `AgctorSDK.Host/wwwroot/js/dashboard/scenario-flow/` (e.g. `graph-document.js`, `graph-renderer.js`, `cytoscape-adapter.js`) |
| JSON Schema source of truth (copy to Host when wiring validation) | [scenario-flow-schema.json](./scenario-flow-schema.json) → optional `AgctorSDK.Host/wwwroot/schemas/scenario-flow.schema.json` |
| Scenario catalog contracts | `AgctorSDK.Host/Services/Scenarios/ScenarioDefinitions.cs` |
| Scenario catalog persistence + validation | `AgctorSDK.Host/Services/Scenarios/JsonScenarioCatalog.cs` |
| Scenario API DTOs | `AgctorSDK.Host/Models/ApiModels.cs` |
| Scenario API endpoints | `AgctorSDK.Host/Controllers/ScenariosController.cs` |
| Persona LLM runner (playground + flow) | `AgctorSDK.Host/Services/ProjectMemory/ProjectMemoryPersonaLlmRunner.cs` |
| Flow graph interpreter + execution service | `AgctorSDK.Host/Services/Scenarios/ScenarioFlowGraphInterpreter.cs`, `ScenarioFlowExecutionService.cs` |
| Host integration tests | `AgctorSDK.Host.IntegrationTests/` |

