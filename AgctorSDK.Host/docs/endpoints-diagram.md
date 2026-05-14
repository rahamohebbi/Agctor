# Endpoints Diagram

![Endpoints Diagram](./endpoints-diagram.jpg)

[Edit source](./endpoints-diagram.mmd)

## Overview

HTTP REST API endpoints and MCP TCP protocol exposed by AgctorSDK.Host.

## REST API Endpoints

### Config – Dashboard (PRD-006 / PRD-010)
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/Config` | Get full Host configuration (runtime, LLM, MCP, tools, scenarios, agent types, **dashboard scenario name**, **per-type enablement**) |

### LLM / Ollama – Dashboard (PRD-015)
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/Llm/models` | List local models (proxies Ollama `GET /api/tags` using configured base URL) |
| PUT | `/api/Llm/default-model` | Body `{ "model": "<name>" }` — merge `Agctor:LLM:DefaultModel` into `appsettings.User.json` and apply via `LLMAgent.ConfigureDefaults` |

### Runtime – Dashboard (PRD-012)
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/runtime` | Live `IActorRuntimeAdapter` (canonical id, stats), configured next-boot values, catalog with capabilities |
| PUT | `/api/runtime` | Body `{ "defaultRuntime", "protoHost?", "protoPort?" }` — merge into `appsettings.User.json`; **`requiresRestart: true`** |

### Agents (`/api/agents`)
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/agents` | Create new agent |
| GET | `/api/agents` | List all agents |
| PUT | `/api/agents/types/{typeName}/enabled` | Body `{ "enabled": bool }` — persist toggle; stop instances when disabled (PRD-010) |
| GET | `/api/agents/{id}` | Get agent info |
| GET | `/api/agents/{id}/detail` | Get agent info + type-specific detail (PRD-006) |
| POST | `/api/agents/{id}/message` | Send message to agent |
| POST | `/api/agents/{id}/message/stream` | SSE: stream LLM deltas + final `done` (PRD-011) |
| GET | `/api/agents/health` | Health check |

### Agent definitions (`/api/agents/definitions`) — PRD-013
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/agents/definitions` | Unified catalog: registered C# agent types + project-memory YAML specs |
| GET | `/api/agents/definitions/{id}` | Detail for a type name or YAML spec id |
| POST | `/api/agents/definitions/project-memory` | Create new `*.agent.yaml` (body `SaveAgentRequestDto`) |
| PUT | `/api/agents/definitions/project-memory/{id}` | Update YAML on disk |
| DELETE | `/api/agents/definitions/project-memory/{id}` | Delete backing YAML |

### Goals (`/api/goals`)
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/goals` | List all goals |
| GET | `/api/goals/{id}` | Get goal by ID |
| POST | `/api/goals` | Create goal |
| PUT | `/api/goals/{id}` | Update goal |
| DELETE | `/api/goals/{id}` | Delete goal |

### Tools (`/api/tools`)
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/tools` | List available tools |
| GET | `/api/tools/{id}` | Get tool info |
| POST | `/api/tools/{id}/invoke` | Invoke tool |
| POST | `/api/tools/batch` | Batch invoke tools |
| GET | `/api/tools/health` | Health check |

### Scenarios catalog (`/api/scenarios`)
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/scenarios` | List merged scenario catalog (optional `flow` GraphDocument per scenario, PRD-014) |
| GET | `/api/scenarios/{id}` | Get one scenario |
| PUT | `/api/scenarios` | Save user catalog; validates flow |
| POST | `/api/scenarios/reload` | Reload catalog from disk |
| POST | `/api/scenarios/{id}/apply` | Apply scenario (spawn/configure per kind) |
| POST | `/api/scenarios/{id}/flow/run` | Execute `flow` (sequential + parallel→Merge); `LlmNode` uses project-memory YAML + Ollama; optional `llmNodeTimeoutSeconds` (default 180) |

### Test (`/api/test`)
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/test/setup-scenario` | Setup test scenario; body may omit `scenarioName` to use `Agctor:Dashboard:ScenarioName` |
| GET | `/api/test/scenarios` | List available scenarios |
| GET | `/api/test/scenarios/{name}` | Get scenario info |
| GET | `/api/test/current-scenario` | Get the current dashboard scenario |

### CodeGraph – Dashboard (PRD-006)
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/CodeGraph/current` | Get current CodeGraph context (actor tree + embedding summary) when code-graph-demo is active; 404 otherwise |
| GET | `/api/CodeGraph/embeddings` | Get embedding records for debugging and visualization |
| GET | `/api/CodeGraph/file-content?path=...` | Preview file content for a file in the active actor tree |

### Chat projects (`/api/chat/projects`)
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/chat/projects` | Create a chat project (`name`, `projectType`, optional `projectId`) |
| GET | `/api/chat/projects` | List projects (newest update first) |
| GET | `/api/chat/projects/{id}` | Get one project |
| PUT | `/api/chat/projects/{id}` | Update name and/or project type |
| DELETE | `/api/chat/projects/{id}` | Delete project (sessions become standalone) |
| GET | `/api/chat/projects/{id}/sessions` | List sessions in this project |

### Chat Sessions (`/api/chat/sessions`)
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/chat/sessions` | Create a chat session (optional `projectId` to create inside a project) |
| GET | `/api/chat/sessions` | List sessions: optional `projectId=` or `standalone=true` |
| GET | `/api/chat/sessions/{id}` | Load session transcript (metadata + turns + summary) |
| PUT | `/api/chat/sessions/{id}/project` | Assign session to a project (body: `{ "projectId" }`) |
| DELETE | `/api/chat/sessions/{id}/project` | Remove session from project (standalone) |

### Project memory (`/api/project-memory`) — PRD-013
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/project-memory/status` | Project root, load summary, agent count |
| GET | `/api/project-memory/agents` | List portable agent specs |
| GET | `/api/project-memory/agents/{id}` | One spec + YAML preview |
| PUT | `/api/project-memory/agents/{id}` | Save agent YAML |
| DELETE | `/api/project-memory/agents/{id}` | Delete agent file |
| GET | `/api/project-memory/templates` | Built-in agent templates |
| POST | `/api/project-memory/agents/from-template` | Create agent from template |
| GET | `/api/project-memory/schema` | Schema bundle (YAML per segment) |
| PUT | `/api/project-memory/schema/{segment}` | Save one schema segment |
| POST | `/api/project-memory/validate` | Validate project (no full rebuild) |
| POST | `/api/project-memory/rebuild` | Full rebuild |
| POST | `/api/project-memory/project-root` | Persist `Agctor:ProjectMemory:ProjectRoot` |
| GET | `/api/project-memory/tree` | Project directory tree (JSON) |
| GET | `/api/project-memory/file?path=` | Preview file under project root |
| POST | `/api/project-memory/playground/run` | One-shot LLM test (optional `sessionId` for transcript context) |
| POST | `/api/project-memory/playground/message/stream` | SSE chat turn; persists user/assistant to chat session store |
| POST | `/api/project-memory/orchestrator/run` | Pipeline: extract → route/write → optional query (`mode`: auto, ingestOnly, queryOnly) |
| POST | `/api/project-memory/generic-inbox/replay` | Replay generic inbox / routing (debug and recovery) |
| GET | `/api/project-memory/workspace/git-changes` | Git working-tree summary for the configured project root |

### Entity resolution review (`/api/project-memory/resolution`) — PRD-018
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/project-memory/resolution/pending` | Pending merge candidates for human review |
| POST | `/api/project-memory/resolution/promote` | Accept / promote a candidate |
| POST | `/api/project-memory/resolution/demote` | Demote a candidate |
| POST | `/api/project-memory/resolution/reject` | Reject a candidate |
| GET | `/api/project-memory/resolution/metrics` | Lightweight resolution metrics snapshot |

## Dashboard (Razor Pages)
- **GET /Dashboard** – Host configuration overview
- **GET /Dashboard/Agents** – Unified agent-type table with toggles and single configured scenario (PRD-010)
- **GET /Dashboard/AgentDetail/{id}** – Agent detail with type-specific view
- **GET /Dashboard/CodeGraph** – CodeGraph actor tree and embedding summary
- **GET /Dashboard/ProjectMemory** – Project memory overview (PRD-013)
- **GET /Dashboard/ProjectMemory/Agents** – Portable agent list
- **GET /Dashboard/ProjectMemory/Agents/Edit** – Agent editor (`?id=` optional)
- **GET /Dashboard/ProjectMemory/Templates** – Template gallery + wizard
- **GET /Dashboard/ProjectMemory/Playground** – Chat transcript + streaming LLM tests for agent specs
- **GET /Dashboard/ProjectMemory/Schema** – Schema studio (YAML tabs)
- **GET /Dashboard/ProjectMemory/Workspace** – Tree + file preview
- **GET /Dashboard/ProjectMemory/Projects** – Manage chat projects and move sessions
- **GET /Dashboard/ProjectMemory/Maintenance** – Project root, validate, rebuild

## MCP Protocol
- **TCP** (`McpListener`): bind address `Mcp:Host` (default `0.0.0.0`) and port `Mcp:Port` (default **8080**; `0` picks an ephemeral port). Newline-delimited JSON messages route through **MessageDispatcher** to agents.

## Swagger
- **GET /swagger**: OpenAPI UI for API exploration
