# Product Requirements Document: AgctorSDK.Core

## 1. Introduction

*   **Product Name:** AgctorSDK.Core
*   **Purpose:** The foundational library for the Agctor SDK, providing the essential interfaces, contracts, and core functionalities for building agentic systems. It enables the development of applications based on the actor model with support for pluggable backend runtimes.
*   **Nature:** While primarily a library, it is compiled as an executable (`OutputType>Exe</OutputType>`), suggesting it may include a test host or a minimal runtime environment.

## 2. Goals

*   To provide a clear and robust set of interfaces for defining actors, agents, messages, and runtime interactions.
*   To enable a "hot-swappable backends" architecture, allowing developers to choose and switch between different actor model implementations (e.g., InMemory, Orleans, Proto.Actor) without altering application logic.
*   To offer a consistent programming model for asynchronous, message-based communication within agentic systems.
*   To facilitate the creation, management, and monitoring of actor and agent lifecycles.
*   To support rich message metadata for advanced routing, correlation, and debugging.
*   To provide a type-safe yet flexible development experience.
*   To serve as the central, indispensable component of the larger Agctor SDK ecosystem.

## 3. Target Audience

*   **Primary:** Developers building applications using the Agctor SDK.
*   **Secondary:** Developers creating custom actor runtime adapters to plug into the Agctor ecosystem.
*   **Tertiary:** Architects designing distributed and agent-based systems.

## 4. Core Components & Interfaces (Functional Requirements)

### FC1: Actors (`IActor`)
*   **FC1.1:** Define a fundamental `IActor` interface.
    *   **FC1.1.1:** Support lifecycle management methods: `InitializeAsync()`, `ShutdownAsync()`.
    *   **FC1.1.2:** Support message processing: `ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken)`.
    *   **FC1.1.3:** Expose actor state (`ActorState` enum: `Initializing`, `Active`, `Inactive`, `Stopping`, `Stopped`, `Faulted`).
    *   **FC1.1.4:** Provide an event (`StateChanged`) for actor lifecycle state changes.
    *   **FC1.1.5:** Each actor must have a unique `Id` and an `ActorType` string.

### FC2: Agents (`IAgent`)
*   **FC2.1:** Define an `IAgent` interface that extends `IActor`.
    *   **FC2.1.1:** Store the `CurrentPrompt` the agent is working on.
    *   **FC2.1.2:** Track `ParentAgentId` and a list of `ChildAgentIds` for hierarchical structures.
    *   **FC2.1.3:** Expose agent-specific status (`AgentStatus` enum: `Idle`, `Working`, `WaitingForSubtasks`, `Completed`, `Failed`).
    *   **FC2.1.4:** Method to process a prompt: `ProcessPromptAsync(string prompt, CancellationToken cancellationToken)`.
    *   **FC2.1.5:** Method to assign subtasks to child agents: `AssignSubtaskAsync(string subtaskPrompt, string? agentType, CancellationToken cancellationToken)`, returning the child agent's ID.
    *   **FC2.1.6:** Methods to handle subtask outcomes: `HandleSubtaskCompletionAsync(...)`, `HandleSubtaskFailureAsync(...)`.
    *   **FC2.1.7:** Provide events for agent status changes (`StatusChanged`), child agent spawning (`ChildAgentSpawned`), and subtask completion (`SubtaskCompleted`).

### FC3: Agent Factory (`IAgentFactory`)
*   **FC3.1:** Define an `IAgentFactory` interface for creating and managing agents.
    *   **FC3.1.1:** Method to spawn new agents (generic `SpawnAgentAsync<TAgent>(...)` and by type name `SpawnAgentAsync(string agentTypeName, ...)`).
        *   _Comment:_ Should handle agent creation, initialization, and readiness for prompt processing.
    *   **FC3.1.2:** Method to retrieve existing agents (generic `GetAgentAsync<TAgent>(...)` and non-generic `GetAgentAsync(...)`).
    *   **FC3.1.3:** Method to stop and remove agents: `StopAgentAsync(...)`.
    *   **FC3.1.4:** Method to generate unique agent IDs: `GenerateAgentId(...)`.
    *   **FC3.1.5:** Expose the underlying `IActorRuntimeAdapter`.

### FC4: Messages (`IMessageEnvelope`, `IMessageMetadata`)
*   **FC4.1:** Define `IMessageEnvelope` for wrapping messages.
    *   **FC4.1.1:** Include a unique `Id` for tracking.
    *   **FC4.1.2:** Contain the message `Payload` (object).
    *   **FC4.1.3:** Include `IMessageMetadata` for system-level information.
    *   **FC4.1.4:** Support custom application-specific `Headers` (read-only dictionary).
    *   **FC4.1.5:** Support immutable update operations (`WithPayload()`, `WithHeaders()`).
*   **FC4.2:** Define `IMessageMetadata` for message system information.
    *   **FC4.2.1:** Include routing info: `SenderId`, `ReceiverId`, `ReplyTo`.
    *   **FC4.2.2:** Include timing info: `Timestamp`, `ExpiresAt`.
    *   **FC4.2.3:** Include `CorrelationId` for linking messages.
    *   **FC4.2.4:** Support message `Priority`.
    *   **FC4.2.5:** Include type information: `MessageType`, `Version`.

### FC5: Actor Runtime Adapter (`IActorRuntimeAdapter`)
*   **FC5.1:** Define `IActorRuntimeAdapter` as the interface for pluggable actor runtime backends.
    *   **FC5.1.1:** Runtime management: `InitializeAsync(config)`, `ShutdownAsync()`, `IsInitialized` property.
    *   **FC5.1.2:** Actor lifecycle operations: `SpawnActorAsync<T>()`, `GetActorAsync<T>()`, `StopActorAsync()`.
    *   **FC5.1.3:** Messaging capabilities: `SendMessageAsync()` supporting fire-and-forget and request-response.
    *   **FC5.1.4:** Monitoring capabilities: `GetActiveActorIdsAsync()`, `GetStatisticsAsync()`.
    *   **FC5.1.5:** Events for runtime monitoring: `ActorSpawned`, `ActorStopped`, `MessageSent`.

### FC6: Actor Runtime Adapter Factory (`IActorRuntimeAdapterFactory`)
*   **FC6.1:** Define `IActorRuntimeAdapterFactory` for creating runtime adapter instances.
    *   **FC6.1.1:** Method to get available runtime names: `GetAvailableRuntimes()`.
    *   **FC6.1.2:** Method to create runtime adapter instances by name: `CreateRuntime(string runtimeName)`.
    *   **FC6.1.3:** Method to create runtime adapter instances by type: `CreateRuntime<T>()`.
    *   **FC6.1.4:** Method to check if a runtime is available: `IsRuntimeAvailable(string runtimeName)`.
    *   **FC6.1.5:** Method to get the default runtime name: `GetDefaultRuntimeName()`.

### FC7: Runtime Statistics (`IRuntimeStatistics`)
*   **FC7.1:** Define `IRuntimeStatistics` for providing runtime health and performance metrics.
    *   **FC7.1.1:** Actor metrics (e.g., active actor count).
    *   **FC7.1.2:** Message metrics (e.g., total processed, messages/sec, avg processing time).
    *   **FC7.1.3:** System metrics (e.g., uptime, memory usage).
    *   **FC7.1.4:** Allow for extensibility with runtime-specific metrics.

### FC8: Dependency Injection Support
*   **FC8.1:** Provide mechanisms for easy integration with `Microsoft.Extensions.DependencyInjection`.
    *   _Comment:_ Likely through extension methods like `services.AddAgctor(...)`.

## 5. Non-Functional Requirements

*   **NFR1: Pluggability & Extensibility:** The core design must strongly support the "hot-swappable backends" principle through `IActorRuntimeAdapter` and `IActorRuntimeAdapterFactory`.
*   **NFR2: Performance:** While abstracting runtimes, the core library should introduce minimal overhead. Asynchronous operations should be efficiently implemented.
*   **NFR3: Asynchronicity:** All potentially blocking operations must be asynchronous (`async`/`await`) and support `CancellationToken`.
*   **NFR4: Immutability:** Message envelopes should favor immutability for safer concurrent processing and message forwarding.
*   **NFR5: Type Safety:** Utilize generics where appropriate to enhance type safety without sacrificing flexibility.
*   **NFR6: Testability:** Interfaces and components should be designed to be easily testable (e.g., allowing mock implementations of adapters and factories).
*   **NFR7: Clarity & Usability:** APIs should be intuitive and well-documented for developers using the SDK.
*   **NFR8: Robustness:** Core components should handle common error conditions gracefully.

## 6. Key Design Principles (from README)

*   Hot-Swappable Backends
*   Immutable Message Envelopes
*   Comprehensive Metadata
*   Async-First
*   Event-Driven Monitoring
*   Type Safety

## 7. Dependencies

*   **Target Framework:** .NET 8.0 (`net8.0`)
*   **Packages:**
    *   `System.Threading.Channels`
    *   `Microsoft.Extensions.DependencyInjection` (and `Abstractions`)
    *   `Microsoft.Extensions.Hosting`
    *   `Microsoft.Extensions.Logging`
    *   `Microsoft.Extensions.Options.ConfigurationExtensions`

## 8. Future Considerations / Potential Enhancements

*   Standardized error handling and reporting mechanisms across agents and runtimes.
*   Built-in support for more complex agent interaction patterns (e.g., sagas, distributed transactions).
*   Enhanced observability features (e.g., distributed tracing integration).
*   Tooling for visualizing agent hierarchies and message flows.
*   Formalized versioning strategy for messages and agent contracts. 