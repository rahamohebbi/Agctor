# Agctor System

A .NET-based actor model framework for building agentic systems with pluggable backends and extensible agent capabilities.

## Overview

The Agctor System provides a robust foundation for developing agentic applications using the actor model pattern. It features:

- **Actor Model Implementation**: Core actor framework with lifecycle management and message processing
- **Pluggable Runtime Architecture**: Hot-swappable backend adapters for different deployment scenarios
- **Agent System**: Extensible agent capabilities with support for LLM integration, tool usage, and human interaction
- **Message Communication Protocol (MCP)**: Standardized message protocol for agent communication
- **HTTP + MCP Integration**: RESTful API with MCP compliance for web and TCP clients
- **Timeout Management**: Comprehensive timeout handling with progress tracking and partial result collection

## Key Features

### 🤖 Agent Types
- **LLM Agents**: Integration with local LLM services (Ollama)
- **Tool Agents**: Multi-language code execution (C#, Python)
- **Human Agents**: CLI-based human interaction for complex tasks

### 🔧 Core Components
- **Actor Runtime**: In-memory runtime with pluggable architecture
- **Agent Factory**: Multi-type agent creation and management
- **Message Dispatcher**: HTTP-to-MCP message routing
- **Timeout Supervisor**: Non-polling timeout management with adaptive policies

### 🌐 API & Integration
- **RESTful API**: Agent operations, tool invocation, and scenario management
- **MCP Server**: TCP listener for protocol compliance
- **OpenAPI/Swagger**: Auto-generated documentation and testing interface
- **CLI Interface**: Command-line utility for prompt submission

## Project Structure

```
AgctorSDK.Core/                    # Core framework and actor model
AgctorSDK.Core.Tests/              # Unit tests
AgctorSDK.Core.IntegrationTests/   # Integration tests
AgctorSDK.Host/                    # HTTP + MCP server application
AgctorCLI/                         # Command-line interface
Demo/                              # Example applications
```

## Quick Start

### 1. Build the Solution
```bash
dotnet build Agctor.sln
```

### 2. Run the Host Application
```bash
cd AgctorSDK.Host
dotnet run
```

### 3. Use the CLI
```bash
cd AgctorCLI
dotnet run -- "Your prompt here"
```

### 4. Access the API
- REST API: `http://localhost:5000`
- MCP Server: `tcp://localhost:8080`
- Swagger UI: `http://localhost:5000/swagger`

## Testing

Run unit tests:
```bash
dotnet test AgctorSDK.Core.Tests
```

Run integration tests:
```bash
dotnet test AgctorSDK.Core.IntegrationTests
```

## Architecture

The Agctor System follows the Actor Model pattern with:
- **Actors**: Independent units of computation with state and behavior
- **Messages**: Asynchronous communication between actors
- **Supervision**: Hierarchical error handling and recovery
- **Location Transparency**: Actors can be local or distributed

## Contributing

This project emphasizes:
- **Actor Model principles**: Leverage actor patterns for concurrent and distributed systems
- **Modular design**: Avoid duplication and create reusable components
- **Documentation**: Add context and explanations to code
- **Testing**: Maintain comprehensive test coverage

## License

[Add your license information here] 