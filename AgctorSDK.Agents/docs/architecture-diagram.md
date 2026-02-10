# Architecture Diagram

![Architecture Diagram](./architecture-diagram.jpg)

[Edit source](./architecture-diagram.mmd)

## Overview

This diagram illustrates the high-level architecture of the AgctorSDK.Agents project, showing the relationships between agents, factories, adapters, and external dependencies.

## Key Components

### Agents Layer
- **BaseActor**: Abstract base class providing core actor functionality
- **Agent**: Base agent implementation with recursive task decomposition capabilities
- **LLMAgent**: Agent that communicates with Ollama LLM service for natural language processing
- **CoderAgent**: Orchestrates code editing, compilation, and testing workflows
- **EchoAgent**: Simple agent that echoes responses (for testing)
- **TracedAgent**: Decorator pattern agent that adds activity tracking
- **HumanAgentAdapter**: Agent that facilitates human interaction in workflows
- **PullRequestAgent**: Specialized agent for managing pull request workflows
- **TaskScoperAgent**: Agent that scopes and breaks down tasks

### Factory & Registry
- **AgentFactory**: Default implementation for creating and managing agent instances
- **TracingAgentFactory**: Factory that wraps agents with tracing capabilities
- **AgentRegistry**: Central registry for tracking and discovering agent instances
- **AgentTypeOptions**: Configuration for agent type registration
- **AgentOptions**: Settings and options for agent behavior

### Runtime Adapters
- **InMemoryActorRuntime**: In-memory actor runtime implementation for local development
- **ProtoActorAdapter**: Proto.Actor runtime adapter for distributed actor systems
- **OrleansAdapter**: Orleans runtime adapter (placeholder for future implementation)

### Task Executors
- **CodeGraphTaskExecutor**: Executor that integrates with CodeGraph agents for code-related tasks
- **PullRequestTaskExecutor**: Executor for pull request workflow tasks

### Tools Models
- **ToolRequest**: Model for tool invocation requests
- **ToolResult**: Model for tool execution results

## Relationships

- Agents inherit from BaseActor and implement IAgent interface from AgctorSDK.Core
- AgentFactory creates agents using runtime adapters and manages their lifecycle
- Agents can spawn child agents for task decomposition (hierarchical agent structure)
- Runtime adapters provide the underlying actor framework integration (InMemory, Proto.Actor, Orleans)
- Task executors coordinate with agents through the registry and factory
- LLMAgent communicates with external Ollama service via HTTP API

## Message Flow

- Agents communicate via message envelopes (MessageEnvelope from Core)
- Parent agents spawn child agents through AgentFactory
- Child agents send completion/failure notifications back to parent agents
- Runtime adapters handle message routing and delivery

## External Dependencies

- **Ollama**: Local LLM service for natural language processing
- **Proto.Actor**: High-performance actor framework (v1.5.0)
- **Orleans**: Microsoft Orleans actor framework (placeholder)
