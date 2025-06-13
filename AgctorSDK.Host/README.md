# AgctorSDK.Host – HTTP + MCP Integration Gateway

The **AgctorSDK.Host** project provides a web gateway interface to the AGCTOR framework, exposing HTTP APIs and an MCP (Model Context Protocol) listener to enable external systems and users to interact with agents, tools, and workflows. It acts as the single entry point into the agent ecosystem.

## Features

### ✅ HTTP REST API
- **Agent Message Routing**: Send messages to agents via `POST /agents/{agentId}/message`
- **Agent Discovery**: List and inspect agents via `GET /agents`
- **Tool Invocation**: Execute tools directly via `POST /tools/{toolId}/invoke`
- **Health Checks**: Monitor system health via `/agents/health` and `/tools/health`
- **Swagger Documentation**: Interactive API docs at `/swagger`

### ✅ MCP (Model Context Protocol) Support
- **TCP Listener**: Accepts MCP connections on port 8080
- **Message Routing**: Routes MCP messages to appropriate agents
- **Protocol Compliance**: Follows MCP standard for metadata and headers
- **Concurrent Connections**: Handles multiple MCP clients simultaneously

### ✅ Actor Model Integration
- **Message Envelopes**: Converts HTTP/MCP requests to `IMessageEnvelope`
- **Runtime Adapter**: Uses `IActorRuntimeAdapter` for agent communication
- **Isolation**: Each connection and request processed independently
- **Error Handling**: Graceful error handling and response formatting

## Project Structure

```
AgctorSDK.Host/
├── Controllers/
│   ├── AgentsController.cs      # Agent endpoints
│   └── ToolsController.cs       # Tool endpoints
├── Models/
│   └── ApiModels.cs            # DTOs and request/response models
├── Services/
│   ├── MessageDispatcher.cs    # Message routing service
│   └── ToolInvoker.cs          # Direct tool execution service
├── Mcp/
│   └── McpListener.cs          # MCP protocol listener
├── Program.cs                  # Application entry point
└── appsettings.json           # Configuration
```

## API Endpoints

### Agent Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/agents/{agentId}/message` | Send message to agent |
| `GET` | `/api/agents` | List all agents |
| `GET` | `/api/agents/{agentId}` | Get agent details |
| `GET` | `/api/agents/health` | Agent system health |

### Tool Execution

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/tools/{toolId}/invoke` | Execute tool |
| `GET` | `/api/tools` | List available tools |
| `GET` | `/api/tools/{toolId}` | Get tool information |
| `POST` | `/api/tools/batch` | Batch tool execution |
| `GET` | `/api/tools/health` | Tool system health |

### Documentation

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/swagger` | Interactive API documentation |

## Quick Start

### 1. Run the Host

```bash
cd AgctorSDK.Host
dotnet run
```

The server will start on `https://localhost:5001` with:
- 📊 Swagger UI at: `https://localhost:5001/swagger`
- 🔌 MCP listener on TCP port 8080

### 2. Send Message to Agent (HTTP)

```bash
curl -X POST "https://localhost:5001/api/agents/my-agent/message" \
  -H "Content-Type: application/json" \
  -d '{
    "payload": {
      "message": "Hello, Agent!",
      "type": "greeting"
    },
    "metadata": {
      "priority": "normal"
    },
    "senderId": "test-client"
  }'
```

### 3. Execute Tool Directly

```bash
curl -X POST "https://localhost:5001/api/tools/file-system/invoke" \
  -H "Content-Type: application/json" \
  -d '{
    "parameters": {
      "operation": "list",
      "path": "/tmp"
    },
    "timeoutSeconds": 10
  }'
```

### 4. Connect via MCP

Connect to `localhost:8080` via TCP and send JSON messages:

```json
{
  "id": "msg-123",
  "targetAgent": "my-agent",
  "payload": {
    "command": "execute",
    "parameters": {"action": "test"}
  },
  "metadata": {
    "priority": "high"
  },
  "headers": {
    "content-type": "application/json"
  }
}
```

## Configuration

Configure via `appsettings.json`:

```json
{
  "Mcp": {
    "Host": "0.0.0.0",
    "Port": 8080
  },
  "Logging": {
    "LogLevel": {
      "AgctorSDK.Host": "Debug"
    }
  }
}
```

## Available Tools

The Host provides access to these tools:

- **file-system**: File operations (read, write, list, delete)
- **code-executor**: Execute code in various languages (Python, C#, JavaScript)
- **code-editor**: Edit and manipulate code files

## Error Handling

All endpoints return structured error responses:

```json
{
  "code": "ERROR_CODE",
  "message": "Human-readable error message",
  "details": {},
  "timestamp": "2024-01-01T00:00:00Z"
}
```

Common error codes:
- `INVALID_AGENT_ID`: Agent identifier is invalid
- `AGENT_NOT_FOUND`: Agent doesn't exist
- `INVALID_PAYLOAD`: Message payload is malformed
- `TOOL_NOT_FOUND`: Tool doesn't exist
- `INVALID_PARAMETERS`: Tool parameters are invalid

## Integration Testing

The project includes comprehensive integration tests:

```bash
# Run Host integration tests
dotnet test AgctorSDK.Host.IntegrationTests

# Test categories:
# - HTTP API endpoints
# - MCP protocol compliance
# - Concurrent request handling
# - Error scenarios
# - Tool execution
```

## Dependencies

- **AgctorSDK.Core**: Core Actor Model framework
- **ASP.NET Core 8.0**: Web framework
- **Swashbuckle**: API documentation
- **System.Net.WebSockets**: MCP protocol support

## Architecture

The Host follows Actor Model principles:

1. **Isolation**: Each HTTP request and MCP connection processed independently
2. **Message Passing**: All communication via message envelopes
3. **No Shared State**: Stateless design with dependency injection
4. **Error Isolation**: Failures in one operation don't affect others

## Future Enhancements

- **WebSocket Support**: Add WebSocket endpoints for real-time communication
- **Authentication**: Add JWT/OAuth2 authentication
- **Rate Limiting**: Implement request rate limiting
- **Metrics**: Add detailed performance metrics
- **Clustering**: Support for multi-instance deployments

## Contributing

1. All new features must include unit and integration tests
2. Follow the Actor Model principles
3. Maintain API documentation with XML comments
4. Ensure MCP protocol compliance
5. Add configuration examples for new features 