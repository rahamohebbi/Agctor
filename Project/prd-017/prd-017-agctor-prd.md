# PRD-017 — Playground hierarchy (projects → sessions → chat)

## Problem

The Playground used a flat grid: optional project `<select>`, global scenario `<select>`, and chat session `<select>`. That hid the hierarchy (project owns scenario and sessions) and made session picking feel like a form field rather than navigation.

## Goals

1. **Project-first:** Operators choose a **project** from a fixed list (rail), then see **sessions** for that project as a visible list (not a `<select>`).
2. **Scenario on the project:** Scenario is chosen when **creating** a project. The active project shows **current scenario** clearly; changing it is an explicit **project-level** action with a short warning (flows/catalog may differ from earlier turns).
3. **Debug panels placement:** **Request pipeline (debug)** and **Trace timeline** stay **below** the transcript and composer (same components and behavior as before PRD-017).
4. **Deep links:** URLs continue to support `?agentId=`, `?projectId=`, `?sessionId=`. If only `sessionId` is present, resolve `projectId` from `GET /api/chat/sessions/{sessionId}` → `session.projectId` when present.

## Non-goals

- Changing SSE payloads, trace schema, or ingest rules.
- New server endpoints (reuse `PUT /api/chat/projects/{projectId}` for scenario updates).

## Layout (desktop)

Three regions in reading order:

1. **Projects (narrow rail):** scrollable project buttons, **New project** block (name + scenario for create).
2. **Sessions (medium column):** list of sessions for the selected project; **New session**, **Refresh**; row click selects session and loads transcript.
3. **Chat column:** optional project header (name + current scenario + **Change scenario**); **Transcript** + composer; then an **Advanced** disclosure (collapsed by default) that exposes an **Agent spec override** scoped to the scenario's personas; then **Request pipeline**; then **Trace timeline**.

**Responsive:** On small viewports regions **stack** vertically (projects → sessions → chat).

## Flows

### Create project

1. Enter name and scenario (required).
2. **New project** → `POST /api/chat/projects` with `{ name, scenarioId }`.
3. New project becomes selected; session list loads; if no sessions, operator uses **New session** (no auto-create until a project exists and user explicitly creates a session, except first session after create optional — implementation may create none until **New session**).

### Select project

1. Click project in rail → `GET /api/chat/projects/{projectId}/sessions`.
2. Highlight project; refresh session list; preserve `sessionId` from URL if it belongs to this project, else select first session or none.

### Change scenario

1. **Change scenario** reveals picker (catalog from `GET /api/scenarios`).
2. Confirm → `PUT /api/chat/projects/{projectId}` with `{ scenarioId }`.
3. Refresh project metadata and header copy.

### Send message

`POST /api/project-memory/playground/message/stream` with `sessionId`, `agentId`, `payload`, and `scenarioId` from the **selected project**'s stored scenario.

The client resolves `agentId` automatically as:

1. Operator override from the **Advanced** disclosure (persisted in the URL as `?agentId=` only when explicit).
2. Active scenario's `personaBindings.extractor` (the ingest-capable persona — the only one that writes to disk).
3. First id in the scenario's `personaAgentIds`.
4. Fallback: the first globally available agent.

Options in the Advanced picker are scoped to the scenario's `personaAgentIds` ∪ flow `LlmNode.config.personaId`, falling back to the full agent list only when the scenario has no roster. Changing or resetting the override re-runs this resolution; changing the project or scenario clears the override.

## Acceptance criteria

- [ ] No chat-session `<select>`; sessions are a clickable list.
- [ ] No project `<select>`; projects are a clickable rail.
- [ ] Scenario for **new** projects is chosen in the create block, not mixed with unrelated global state.
- [ ] With a project selected, current scenario is visible; change uses `PUT` and confirmation copy.
- [ ] Flow + trace blocks appear **below** transcript in the page.
- [ ] `?sessionId=` without `?projectId=` selects the project when `session.projectId` is set.
- [ ] Standalone session (no `projectId`): transcript can still load; session list may be empty with explanatory hint.
- [ ] No agent picker in the top bar; sending a message uses the scenario's default agent without any user input.
- [ ] Advanced disclosure shows only the scenario's personas and a Reset link; changing the project or scenario clears the override.

## Manual QA checklist

1. Create two projects with different scenarios; confirm each lists only its sessions.
2. New session under project B does not appear under project A.
3. Change scenario on a project; send a message; confirm stream still completes.
4. Copy link with project + session; open in new tab; selection matches.
5. Resize to narrow width; confirm stacked layout remains usable.

## Wireframe reference

Operator-provided mockup: projects rail + “Selected project” area with session list, then drill-in to session with a way back (stacked layout on narrow screens satisfies “back to list” by scrolling).
