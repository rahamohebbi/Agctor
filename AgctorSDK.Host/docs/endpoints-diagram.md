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

### Chat Sessions (`/api/chat/sessions`)
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/chat/sessions` | Create a new chat session |
| GET | `/api/chat/sessions` | List chat sessions (newest first) |
| GET | `/api/chat/sessions/{id}` | Load session transcript (metadata + turns + summary) |

## Dashboard (Razor Pages)
- **GET /Dashboard** – Host configuration overview
- **GET /Dashboard/Agents** – Unified agent-type table with toggles and single configured scenario (PRD-010)
- **GET /Dashboard/AgentDetail/{id}** – Agent detail with type-specific view
- **GET /Dashboard/CodeGraph** – CodeGraph actor tree and embedding summary

## MCP Protocol
- **TCP port 8080**: Accepts newline-delimited JSON messages
- Routes messages to agents via actor runtime

## Swagger
- **GET /swagger**: OpenAPI UI for API exploration
