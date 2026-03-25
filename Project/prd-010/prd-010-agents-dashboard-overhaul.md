# PRD-010: Agents dashboard overhaul

## Specification status

**Implemented** — see [prd-010-implementation-plan.md](./prd-010-implementation-plan.md) for phases.

## Purpose

Replace the split **Registered types** / **Active agents** dashboard with a **single Flowbite-based list** of agent types from Host configuration, each with an **enable/disable** control persisted to user configuration, and simplify scenario activation to **one configured scenario** for the dashboard.

**Related:** [PRD-006: Host configuration dashboard](../prd-006/prd-006-dashboard.md)

## Scope

- **In-scope:** Unified Agents page UX (Flowbite table/toggles), `GET /api/Config` extensions for dashboard scenario name and enablement map, `PUT /api/agents/types/{typeName}/enabled`, writable `appsettings.User.json`, stopping running agents when a type is disabled, skipping disabled types during scenario setup, single scenario name from `Agctor:Dashboard:ScenarioName`.
- **Out-of-scope:** CodeGraph page redesign; multi-tenant settings; editing committed `appsettings.json` in the repo.

## Goals

- One list: each **registered agent type** (from `AgentTypeOptions`) with live instance count and links to detail.
- **Persisted** enable/disable (default enabled) via configuration merge file.
- **Disable** tears down running instances of that type (`IAgentFactory.StopAgentAsync`).
- **Single scenario** on the dashboard: one Apply action using the configured scenario name.
- **Re-enable** does not auto-spawn; user applies the scenario again.

## Non-goals

- Removing scenario classes from `ScenarioFactory` (tests and API may still reference multiple scenarios by name).
- Guaranteeing CodeGraph demo completeness when core types (e.g. `LLMAgent`) are disabled — partial runs are acceptable; errors surface via existing scenario responses.

## Requirements

### R1 — Unified agent type list

- Rows = keys of `HostConfigurationDto.AgentTypes`.
- Columns: type name, enabled toggle, runtime instances (count + links to `/Dashboard/AgentDetail/{id}`).

### R2 — Persistence

- User overrides live in `appsettings.User.json` (gitignored) under `Agctor:AgentTypeEnablement` and `Agctor:Dashboard:ScenarioName`.
- Host loads this file with `reloadOnChange: true`.

### R3 — Disable behavior

- On disable: persist `false`, then for each running agent whose CLR type name matches the logical type key, call `StopAgentAsync`.

### R4 — Scenario setup

- Scenario implementations consult enablement for **known** registered types; unknown actor types (e.g. CodeGraph-only agents) are not toggled from the dashboard and remain always eligible unless we add them to the registry later.

### R5 — Single dashboard scenario

- `Agctor:Dashboard:ScenarioName` defaults in committed `appsettings.json` (e.g. `code-graph-demo`).
- Agents page does not show a scenario dropdown.

## Acceptance criteria

- [ ] Dashboard shows one table/cards list with Flowbite components; no separate “Registered types” vs “Active agents” sections.
- [ ] Toggling disabled persists to `appsettings.User.json` and removes matching agents from the runtime.
- [ ] Apply uses the configured scenario name without user picking from a list.
- [ ] `GET /api/Config` includes dashboard scenario name and merged enablement for the UI.
- [ ] CodeGraph page behavior unchanged (no regression in navigation or graph load).
