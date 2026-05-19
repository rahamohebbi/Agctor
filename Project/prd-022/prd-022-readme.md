# PRD-022 — Companion Phase 4 (inbox + privacy; no calendar)

**Status:** Delivered (v1) — **Confirmation Inbox** UI/API and **Forget / export / privacy** controls. Calendar/contacts import is explicitly deferred.

## Documents

| File | Purpose |
| --- | --- |
| [prd-022-agctor-prd.md](./prd-022-agctor-prd.md) | Goals, requirements, acceptance criteria |
| [prd-022-implementation-plan.md](./prd-022-implementation-plan.md) | Phased delivery and file map |

## Scope

| Track | Deliverable |
| --- | --- |
| **022a** | `GET` pending inbox, `POST` approve/reject, playground **Confirmation inbox** panel |
| **022b** | Forget person/session hooks, scenario export, `companion-privacy.yaml` settings + playground **Privacy** strip |
| **Deferred** | Google/Outlook calendar, contacts import |

## Related PRDs

- **PRD-019** — Generic inbox store (`pending.yaml` / `confirmed.yaml`), chat yes/no confirm
- **PRD-021** — Session-end auto-ingest (governed by privacy toggle)
- **PRD-020** — Actor/message patterns (inbox review actor facade)

## Key code

| Area | Location |
| --- | --- |
| Inbox decisions | `AgctorSDK.Core/ProjectMemory/Inbox/` |
| Privacy | `AgctorSDK.Core/ProjectMemory/Privacy/` |
| APIs | `AgctorSDK.Host/Controllers/ProjectMemoryController.cs` |
| Playground UI | `Playground.cshtml`, `project-memory-playground.js` |
