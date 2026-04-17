# PRD-016 — Scenario persona persistence and playground trace debugging

**Folder status:** Active — specification and phased plan for **reliable “save to markdown” behavior** in scenario-based playground runs and for **clearer trace drill-down** (input vs outcome) on Project Memory / Playground.

## Documents

| File | Purpose |
| --- | --- |
| [prd-016-agctor-prd.md](./prd-016-agctor-prd.md) | Goals, current behavior, requirements, acceptance criteria |
| [prd-016-implementation-plan.md](./prd-016-implementation-plan.md) | Phased delivery, modules, risks, tests |

## Relationship to other PRDs

- **PRD-013 / PRD-014**: Scenario catalog, flow graphs, and designer; this PRD assumes flows can include Router → PersonaCall → Output and that routing rules steer which agent runs.
- **PRD-009**: Broader trace-timeline UX backlog; PRD-016 scopes a **narrow, high-signal** improvement: **structured Input / Outcome sections** for existing playground span payloads and any small DTO extensions needed.
- **PRD-008**: Historical trace linking; unchanged here.

## Key code locations (baseline before work)

| Area | Location |
| --- | --- |
| Playground SSE + flow + ingest gate | `AgctorSDK.Host/Controllers/ProjectMemoryController.cs` |
| Trace payload JSON for playground spans | `AgctorSDK.Host/Services/ProjectMemory/PlaygroundTraceTimelineDetail.cs` |
| Trace timeline UI (drill-down HTML) | `AgctorSDK.Host/Pages/Shared/Components/TraceTimeline/Default.cshtml` |
| Playground client (refresh timeline after `done`) | `AgctorSDK.Host/wwwroot/js/dashboard/project-memory-playground.js` |
| Person extractor agent spec (sample) | `samples/people-project/.agctor/agents/people/person-extractor.agent.yaml` |
| Operator copy (pipeline note) | `AgctorSDK.Host/Pages/Dashboard/ProjectMemory/Playground.cshtml` |
