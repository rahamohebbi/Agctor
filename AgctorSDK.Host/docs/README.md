# AgctorSDK.Host Documentation

ASP.NET Core Web API + MCP server for the AGCTOR framework.

## Dashboard (PRD-006)

A read-only configuration dashboard is available at **/Dashboard** (Razor Pages + JavaScript). It shows:

- **Overview** – Runtime, LLM, MCP, paths, background services, registered agent types, tools, scenarios (from `GET /api/Config`).
- **Agents** – Live list of active agents and registered types; surfaces backend setup errors.
- **Agent detail** – Per-agent type-specific view (e.g. LLM URL/model for LLMAgent, tools pipeline for CoderAgent) via `GET /api/agents/{id}/detail`.
- **CodeGraph** – Actor tree, embedding lifecycle summary, and file preview when the `code-graph-demo` scenario is active (`GET /api/CodeGraph/current`).

See [endpoints-diagram](./endpoints-diagram.md) for the full API including dashboard endpoints.

## Generating Images

```bash
../../scripts/generate-images.sh AgctorSDK.Host/docs
```
