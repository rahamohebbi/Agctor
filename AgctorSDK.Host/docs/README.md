# AgctorSDK.Host Documentation

ASP.NET Core Web API + MCP server — the main HTTP and MCP gateway for the AGCTOR framework.

## Dashboard (PRD-006)

A read-only configuration dashboard is available at **/Dashboard** (Razor Pages + JavaScript). It shows:

- **Overview** – Runtime, LLM, MCP, paths, background services, registered agent types, tools, scenarios (from `GET /api/Config`).
- **Agents** – List of agents and registered types; links to agent detail.
- **Agent detail** – Per-agent type-specific view (e.g. LLM URL/model for LLMAgent, tools pipeline for CoderAgent) via `GET /api/agents/{id}/detail`.
- **CodeGraph** – Actor tree and embedding store summary when the code-graph-demo scenario is active (`GET /api/CodeGraph/current`).

See [endpoints-diagram](./endpoints-diagram.md) for the full API including dashboard endpoints.

## Generating Images

```bash
../../scripts/generate-images.sh AgctorSDK.Host/docs
```
