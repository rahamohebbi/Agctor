# AgctorSDK.Host Documentation

ASP.NET Core Web API + MCP server for the AGCTOR framework.

## Dashboard (PRD-006)

A read-only configuration dashboard is available at **/Dashboard** (Razor Pages + JavaScript). It shows:

- **Overview** – Runtime, LLM, MCP, paths, background services, registered agent types, tools, scenarios (from `GET /api/Config`).
- **Agents** – Live agents, type toggles, scenario apply, YAML definitions, and **dynamic tool access per agent** (`GET /api/agents/definitions/tool-usage`; UI in `wwwroot/js/dashboard/agents-page.js`).
- **Tools** – **Dashboard / Tools** (`/Dashboard/Tools`): host tools, descriptions from `AgctorToolCatalog`, and **which agents use each tool** (`GET /api/tools/agent-associations`; `wwwroot/js/dashboard/tools-page.js`).
- **Agent detail** – Per-agent type-specific view (e.g. LLM URL/model for LLMAgent, tools pipeline for CoderAgent) via `GET /api/agents/{id}/detail`.
- **CodeGraph** – Actor tree, embedding lifecycle summary, and file preview when the `code-graph-demo` scenario is active (`GET /api/CodeGraph/current`).

See [endpoints-diagram](./endpoints-diagram.md) for the full API including dashboard endpoints.

## Project Memory Playground (`/Dashboard/ProjectMemory/Playground`)

- **Streaming chat** via `POST /api/project-memory/playground/message/stream` (SSE: `flow_plan`, `flow_step`, `phase`, `llm_delta`, `done`).
- **Scenario flows** (PRD-014): router modes, branch execution (`parallel` / `sequential` / `auto`), merge **outputPolicy** (`ranked`, `merge_sections`, `first_non_empty`).
- **Extract replies**: after successful ingest, users see grouped facts from **`IngestUserMessageFormatter`** (see Core docs)—not only a single markdown path.
- **Trace panel**: shared **`TraceTimeline`** component; nested agent/tool rows, tool summary chips, and drill-down for LLM I/O and tool parameters. Client: `wwwroot/js/dashboard/project-memory-playground.js`.

## Generating Images

```bash
../../scripts/generate-images.sh AgctorSDK.Host/docs
```
