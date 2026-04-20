# PRD-017 — Playground hierarchy (projects → sessions → chat)

**Folder status:** Active — UX and information architecture for the **Project Memory / Playground** page: project-first navigation, fixed session list, scenario bound to the project, and debug panels below the transcript.

## Documents

| File | Purpose |
| --- | --- |
| [prd-017-agctor-prd.md](./prd-017-agctor-prd.md) | Problem, layout, flows, acceptance criteria, manual QA |

## Relationship to other PRDs

- **PRD-016 / PRD-013**: Playground streaming, flow chips, and trace timeline behavior are unchanged; this PRD only changes **layout and selection UX**.
- **PRD-013**: Session/project REST APIs (`/api/chat/projects`, `/api/chat/sessions`) remain the contracts.

## Key code locations

| Area | Location |
| --- | --- |
| Playground markup | `AgctorSDK.Host/Pages/Dashboard/ProjectMemory/Playground.cshtml` |
| Playground client | `AgctorSDK.Host/wwwroot/js/dashboard/project-memory-playground.js` |
| Project CRUD + list sessions | `AgctorSDK.Host/Controllers/ChatProjectsController.cs` |
| Session transcript (deep link `projectId`) | `AgctorSDK.Host/Controllers/ChatSessionsController.cs` |
