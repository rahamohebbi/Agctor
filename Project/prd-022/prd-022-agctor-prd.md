# PRD-022: Companion Phase 4 — Confirmation inbox and privacy

## 1. Overview

Phase 4 adds **human review** before ambiguous facts land in people markdown, and **privacy controls** so users can export or remove data. External calendar/contacts import is out of scope.

## 2. Goals

| ID | Goal |
| --- | --- |
| G1 | Operators see **pending generic-inbox rows** for the active scenario and approve/reject without typing yes/no in chat. |
| G2 | Approved rows follow the existing path: `confirmed.yaml` → **replay** → entity `profile.md` / `timeline.md`. |
| G3 | **Forget person** hard-deletes `scenarios/<id>/people/<entity>/` after confirmation. |
| G4 | **Export scenario** downloads people workspace files as a zip. |
| G5 | **Privacy settings** persist under `.agctor/runtime/companion-privacy.yaml` (auto-ingest on session end on/off). |

## 3. Non-goals

- Calendar or contacts connectors
- Multi-tenant auth / GDPR legal copy
- Inbox for every direct curator write (only generic-inbox / route-miss queue)

## 4. Requirements

### 4.1 Confirmation inbox (022a)

| ID | Requirement |
| --- | --- |
| I1 | `GET /api/project-memory/generic-inbox/pending?scenarioId=` returns filtered pending rows. |
| I2 | `POST /api/project-memory/generic-inbox/decide` accepts `{ scenarioId, decisions: [{ proposalId, approve }] }`. |
| I3 | Approve uses `IGenericInboxStore.PersistApprovedAsync` + `IGenericInboxReplayService.ReplayAsync`. |
| I4 | Reject uses `IGenericInboxStore.DropPendingAsync`. |
| I5 | Playground shows inbox panel with count, Approve/Reject per row, refresh after Send. |

### 4.2 Privacy (022b)

| ID | Requirement |
| --- | --- |
| P1 | `GET/PUT /api/project-memory/privacy/settings` reads/writes companion privacy YAML. |
| P2 | `POST /api/project-memory/privacy/forget-person` deletes entity folder; optional clear project focus. |
| P3 | `GET /api/project-memory/privacy/export?scenarioId=` returns zip of scenario `people/` tree. |
| P4 | `ChatSessionsController` respects `AutoIngestOnSessionEnd` before PRD-021 ingest. |
| P5 | Playground Privacy strip: settings toggle, forget-person select + confirm, export button. |

## 5. Acceptance criteria

1. Pending row visible in playground after route-miss / review-queue ingest; Approve updates entity markdown.
2. Reject removes row from pending without writing files.
3. Forget person removes folder; export returns non-empty zip for populated scenario.
4. Disabling auto-ingest skips session-end ingest on checkpoint/delete.
5. Core unit tests for inbox decide + privacy settings; build passes.

## 6. Defaults

- Inbox panel: under **Reminders** on playground (same column).
- Forget: **hard delete** entity directory with browser `confirm()`.
- Auto-ingest: **enabled** by default (matches PRD-021 behavior).
