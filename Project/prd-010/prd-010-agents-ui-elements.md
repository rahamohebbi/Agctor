# PRD-010: Agents UI and API inventory

## Purpose

Align dashboard Agents page with Host APIs and Flowbite components (PRD-010).

## APIs

| Element | Endpoint | Notes |
| --- | --- | --- |
| Host config | `GET /api/Config` | Includes `dashboardScenarioName`, `agentTypeEnablement` (merged defaults). |
| Runtime agents | `GET /api/agents` | Group by `type` for counts and detail links. |
| Enable toggle | `PUT /api/agents/types/{typeName}/enabled` | Body `{ "enabled": true \| false }`. |
| Current scenario | `GET /api/Test/current-scenario` | Label strip. |
| Apply scenario | `POST /api/Test/setup-scenario` | Body `{ "scenarioName": "<from config>", "parameters": {} }` or omit name when server uses config. |

## UI (Flowbite)

| Component | Role |
| --- | --- |
| Card / section | Scenario strip: description, Apply, Refresh. |
| Table | Agent type rows: name, toggle, instance links. |
| Toggle | Per-type enabled (Flowbite peer/toggle pattern). |
| Badge | Instance count. |
| Alert | Backend errors (existing red panel pattern). |

## Out of scope

- CodeGraph page layout and `codegraph-page.js` (smoke only).
