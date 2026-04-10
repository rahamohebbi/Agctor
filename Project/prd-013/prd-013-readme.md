# PRD-013 — Generic Agents + File-Canonical Project Memory

**Folder status:** Active — specification and implementation plan live here; MVP is implemented in the SDK (see below).

## Documents

| File | Purpose |
| --- | --- |
| [prd-013-agctor-prd.md](./prd-013-agctor-prd.md) | Full PRD: goals, `.agctor/` layout, schemas, agents, memory intents, rebuild, UX, MVP vs Phase 2 |
| [prd-013-implementation-plan.md](./prd-013-implementation-plan.md) | Architecture, module placement, delivery sequence, acceptance mapping |
| [prd-013-ux-ui.md](./prd-013-ux-ui.md) | **UX/UI only:** Dashboard Agent Studio, storage rules (Schema Studio), templates, workspace browser, import/rebuild flows — no code |
| [prd-013-ux-ui-implementation-plan.md](./prd-013-ux-ui-implementation-plan.md) | **Engineering plan:** phases UX-A–D, APIs, Razor routes, Host services, tests, docs |
| [prd-013-multi-agent-orchestration-plan.md](./prd-013-multi-agent-orchestration-plan.md) | **Multi-agent orchestration:** extract → route → write → retrieve → reason; phased delivery, contracts, acceptance |
| [prd-013-scenario-catalog-and-chat-linking.md](./prd-013-scenario-catalog-and-chat-linking.md) | **Scenario catalog + chat binding:** JSON-driven scenarios (`agctor-scenarios.json`), `people` scenario, dashboard editor for agent rosters, chat projects linked to `scenarioId` |
| [prd-013-agent-studio-integration-and-scenario-defaults.md](./prd-013-agent-studio-integration-and-scenario-defaults.md) | **Agent page integration plan:** unify `/Dashboard/Agents` + `/Dashboard/ProjectMemory/Agents`, make YAML agents first-class, add scenario selection on main Agents page while preserving default scenario behavior |

## Relationship to other PRDs

- **PRD-012** (actor runtime dashboard): orthogonal; project memory does not change runtime selection.
- **Session / memory PRDs** (e.g. PRD-006, PRD-010): session storage is separate from **canonical project files** under `.agctor/`; portable project truth stays on disk per PRD-013.
- **Scenario catalog PRD:** extends **Host** dashboard scenario loading and **chat session projects**; it does not change `.agctor/` on-disk project memory layout.
- **Agent Studio integration PRD:** unifies dashboard surfaces for runtime agents and project-memory agent definitions without changing actor runtime fundamentals.

## Implemented summary (MVP)

- **`AgctorSDK.Core/ProjectMemory`**: `ProjectLoader`, registries, `DocumentParser`, `MemoryIntentProcessor`, `DocumentProjectionService` (replace_section, merge_list, append_chronological), `RebuildCoordinator`, `IRuntimeIndexStore` with SQLite + Postgres implementations, `GlobMatcher` / `ProjectMemoryOperations` with YAML `memoryAccess` guards.
- **`AgctorSDK.Agents/Agents/ProjectMemory`**: `PersonExtractorProjectAgent`, `MemoryCuratorProjectAgent`, `PersonQueryProjectAgent` (actors; use `ProjectMemoryServiceAccessor` for DI).
- **`AgctorSDK.Tools`**: `ProjectMemoryTool` (read/write document, load schema, search entities).
- **`AgctorSDK.Host`**: `AddAgctorProjectMemory()`, `ProjectMemoryServiceAccessor.Initialize`, default `Agctor:ProjectMemory:ProjectRoot` → repo `samples/people-project`, registered agent types.
- **Sample**: [`samples/people-project`](../../samples/people-project/) (People project type, two entities, templates, agents).
- **Tests**: `AgctorSDK.Core.Tests/ProjectMemory`, `AgctorSDK.Core.IntegrationTests/ProjectMemory`.
- **Docs**: [`AgctorSDK.Core/docs`](../../AgctorSDK.Core/docs/) — architecture / class / dependencies / endpoints diagrams (`*.mmd`, `*.jpg`, `npm run diagrams` in `docs/`).

## Phase 2 (not implemented)

Sales/Jobs project types, view helpers, Agent/Schema Studio UIs, richer validation, optional semantic retrieval — see [PRD §25](./prd-013-agctor-prd.md). **Dashboard UX** for Agent Studio, storage rules, and templates is specified separately in [prd-013-ux-ui.md](./prd-013-ux-ui.md) (specification only until planned).

## Key code locations

| Area | Location |
| --- | --- |
| Loaders, models, YAML | `AgctorSDK.Core/ProjectMemory/` |
| DI extension | `AgctorSDK.Core/DependencyInjection/ProjectMemoryServiceExtensions.cs` |
| Service accessor (host wiring) | `AgctorSDK.Core/ProjectMemory/ProjectMemoryServiceAccessor.cs` |
| SQLite / Postgres stores | `AgctorSDK.Core/ProjectMemory/Indexing/` |
| Project-memory agents | `AgctorSDK.Agents/Agents/ProjectMemory/` |
| Project-memory tool actor | `AgctorSDK.Tools/Tools/Implementations/ProjectMemoryTool.cs` |
| Host registration | `AgctorSDK.Host/Program.cs` |
| Sample project | `samples/people-project/` |
