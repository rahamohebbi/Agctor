# Endpoints Diagram

![Endpoints Diagram](./endpoints-diagram.jpg)

[Edit source](./endpoints-diagram.mmd)

## Overview

HTTP REST API endpoints and MCP TCP protocol exposed by AgctorSDK.Host.

## REST API Endpoints

### Agents (`/api/agents`)
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/agents` | Create new agent |
| GET | `/api/agents` | List all agents |
| GET | `/api/agents/{id}` | Get agent info |
| POST | `/api/agents/{id}/message` | Send message to agent |
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
| POST | `/api/test/setup-scenario` | Setup test scenario |
| GET | `/api/test/scenarios` | List available scenarios |
| GET | `/api/test/scenarios/{name}` | Get scenario info |

## MCP Protocol
- **TCP port 8080**: Accepts newline-delimited JSON messages
- Routes messages to agents via actor runtime

## Swagger
- **GET /swagger**: OpenAPI UI for API exploration
