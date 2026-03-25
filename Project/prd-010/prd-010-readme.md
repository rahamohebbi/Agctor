# PRD-010 — Agents dashboard overhaul

**Folder status:** Active — implementation tracks this specification.

## Documents

| File | Purpose |
| --- | --- |
| [prd-010-agents-dashboard-overhaul.md](./prd-010-agents-dashboard-overhaul.md) | Full PRD: goals, UX, persistence, single scenario, acceptance criteria |
| [prd-010-implementation-plan.md](./prd-010-implementation-plan.md) | Phased delivery plan (includes CodeGraph demo / compile follow-up) |
| [prd-010-agents-ui-elements.md](./prd-010-agents-ui-elements.md) | UI and API inventory for frontend/backend alignment |
| [prd-010-codegraph-demo-and-compile.md](./prd-010-codegraph-demo-and-compile.md) | Default scenario workspace layout, `dotnet build` compile gate, Coder test path |

## Relationship to PRD-006

- **PRD-006** introduced the Host configuration API, Agents list, scenario Apply, and Agent detail. **PRD-010** replaces the split “Registered types / Active agents” UX with a unified Flowbite list, adds persisted per-type enable/disable, ties disable to stopping runtime agents, and configures a **single** dashboard scenario.

## When changing behavior

1. Update enablement persistence only through the Host API (writes `appsettings.User.json` or documented path).
2. Re-enabling a type does **not** auto-spawn instances; user clicks **Apply scenario** again.
3. If you change the **code-graph-demo** temp workspace or compile/test flow, update [prd-010-codegraph-demo-and-compile.md](./prd-010-codegraph-demo-and-compile.md) and keep `CoderAgent` / `CompileTool` / `CodeGraphDemoScenario` in sync.
