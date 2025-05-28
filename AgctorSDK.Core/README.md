# AgctorSDK.Core

This is the core library for the Agctor SDK, providing the fundamental interfaces and contracts for building agentic systems using pluggable actor model backends.

## Core Interfaces

### IActor
The fundamental interface that all actors must implement. Provides:
- **Lifecycle Management**: `InitializeAsync()`, `ShutdownAsync()`
- **Message Processing**: `ReceiveAsync(IMessageEnvelope envelope)`
- **State Tracking**: `State` property with `ActorState` enum
- **Event Notifications**: `StateChanged` event for monitoring actor lifecycle

### IMessageEnvelope
Represents a message envelope that wraps actor messages with metadata and routing information:
- **Message Identity**: Unique `Id` for tracking and correlation
- **Payload**: The actual message content (`object Payload`)
- **Metadata**: System-level routing and timing information (`IMessageMetadata`)
- **Headers**: Custom application-specific properties (`IReadOnlyDictionary<string, object>`)
- **Immutable Operations**: `WithPayload()` and `WithHeaders()` for creating modified copies

### IMessageMetadata
Contains system-level information about messages:
- **Routing**: `SenderId`, `ReceiverId`, `ReplyTo`
- **Timing**: `Timestamp`, `ExpiresAt`
- **Correlation**: `CorrelationId` for linking related messages
- **Priority**: Message priority for queue ordering
- **Type Information**: `MessageType`, `Version` for serialization and compatibility

### IActorRuntimeAdapter
The adapter interface for pluggable actor runtime backends (Orleans, Proto.Actor, wasmCloud, etc.):
- **Runtime Management**: `InitializeAsync()`, `ShutdownAsync()`, `IsInitialized`
- **Actor Lifecycle**: `SpawnActorAsync<T>()`, `GetActorAsync<T>()`, `StopActorAsync()`
- **Messaging**: `SendMessageAsync()` with fire-and-forget and request-response patterns
- **Monitoring**: `GetActiveActorIdsAsync()`, `GetStatisticsAsync()`
- **Events**: `ActorSpawned`, `ActorStopped`, `MessageSent` for runtime monitoring

### IRuntimeStatistics
Provides runtime health and performance metrics:
- **Actor Metrics**: Active actor count
- **Message Metrics**: Total processed, messages per second, average processing time
- **System Metrics**: Uptime, memory usage
- **Extensibility**: Additional runtime-specific metrics

## Actor States

The `ActorState` enum defines the possible states during an actor's lifecycle:
- `Initializing`: Actor is being created
- `Active`: Actor is processing messages
- `Inactive`: Actor is temporarily inactive but can be reactivated
- `Stopping`: Actor is shutting down
- `Stopped`: Actor has been stopped
- `Faulted`: Actor encountered an error

## Key Design Principles

1. **Hot-Swappable Backends**: The `IActorRuntimeAdapter` interface allows switching between different actor model implementations without changing application code.

2. **Immutable Message Envelopes**: Message envelopes support immutable operations for safe message transformation and forwarding.

3. **Comprehensive Metadata**: Rich metadata support enables advanced routing, correlation, and debugging capabilities.

4. **Async-First**: All operations are asynchronous with proper cancellation token support.

5. **Event-Driven Monitoring**: Events provide visibility into actor lifecycle and message flow for debugging and monitoring.

6. **Type Safety**: Generic constraints ensure type safety while maintaining flexibility.

## Usage Example

```csharp
// Define a custom actor
public class MyActor : IActor
{
    public string Id { get; }
    public string ActorType => nameof(MyActor);
    public ActorState State { get; private set; }
    
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;
    
    public async Task ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        // Process the message
        var message = envelope.Payload;
        // ... handle message logic
    }
    
    // ... implement other IActor methods
}

// Use with a runtime adapter
var adapter = new SomeActorRuntimeAdapter();
await adapter.InitializeAsync(configuration);

var actor = await adapter.SpawnActorAsync<MyActor>("my-actor-1");
await adapter.SendMessageAsync("my-actor-1", "Hello, Actor!");
```

This design enables building scalable, distributed agentic systems with pluggable actor model backends while maintaining a consistent programming model. 