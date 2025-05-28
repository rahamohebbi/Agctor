# AGCTOR
AGCTOR = Agentic Actor Model

An Agentic framework where:  
- Agents (actors) are logic units.
- Actor model backends (e.g., Orleans, Proto.Actor, Akka.NET, in-process actors, WASM actors) are swappable.
- Your system offers a stable interface (IAgent, IActorRuntime) and runtime-agnostic messaging.

## Architecture

```
+---------------------+
|     Agent Logic     |   <-- Your AI/agent code (business logic)
|  (IAgent interface) |
+----------+----------+
           |
           v
+---------------------+
|   Actor Abstraction |   <-- Unified Actor runtime interface
| (IActorRuntime, etc)|
+----------+----------+
           |
   +-------+-------+--------------------------+
   |               |                          |
   v               v                          v
+----------+   +------------+           +------------+
| Orleans  |   | Proto.Actor|           | InMemory   |   <-- Backends
| Adapter  |   | Adapter    |           | Adapter    |
+----------+   +------------+           +------------+
```

## Project Structure

- **AgctorSDK.Core/**: Core SDK library containing interfaces, abstractions, and runtime implementations
- **AgctorCLI/**: Command-line interface tool for managing and interacting with agents

## Prerequisites

- .NET 8.0 SDK or later
- Git

## Getting Started

### Clone and Build

```bash
git clone <repository-url>
cd AGCTOR
dotnet restore
dotnet build
```

### Run the CLI Tool

```bash
dotnet run --project AgctorCLI
```

## Development

### Building the Solution

```bash
# Build all projects
dotnet build

# Build in Release mode
dotnet build --configuration Release
```

### Running Tests

```bash
dotnet test
```

### Git Hooks

This project includes a pre-commit hook that automatically builds the solution before allowing commits. This ensures that only compilable code is committed to the repository.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Ensure all builds pass
5. Submit a pull request

## License

[Add your license information here]
