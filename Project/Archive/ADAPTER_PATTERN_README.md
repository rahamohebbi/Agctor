# Agctor SDK - Adapter Pattern Implementation

## Overview

The Agctor SDK implements a comprehensive adapter pattern system that allows plugging different actor runtime backends (Orleans, Proto.Actor, etc.) while maintaining a consistent API. This design enables hot-swappable actor model implementations and provides flexibility for different deployment scenarios.

## Architecture

### Core Components

1. **IActorRuntimeAdapter** - The main interface that all runtime adapters must implement
2. **IActorRuntimeAdapterFactory** - Factory interface for creating runtime adapters
3. **ActorRuntimeAdapterFactory** - Concrete factory implementation using dependency injection
4. **ServiceCollectionExtensions** - Extension methods for DI registration
5. **AgctorOptions** - Configuration options for the actor system

### Available Adapters

| Adapter | Status | Description |
|---------|--------|-------------|
| **InMemoryActorRuntime** | ✅ **Implemented** | Full-featured in-memory actor runtime for development and testing |
| **OrleansAdapter** | ⚠️ **Placeholder** | Microsoft Orleans integration (throws `NotImplementedException`) |
| **ProtoActorAdapter** | ⚠️ **Placeholder** | Proto.Actor integration (throws `NotImplementedException`) |

## Usage

### Basic Setup with Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using AgctorSDK.Core.DependencyInjection;

// Configure services
var services = new ServiceCollection();

// Register Agctor with default InMemory runtime
services.AddAgctor(options =>
{
    options.DefaultRuntime = "InMemory";
    options.MaxConcurrentMessages = 1000;
    options.EnableDetailedLogging = true;
});

var serviceProvider = services.BuildServiceProvider();

// Get the runtime adapter
var runtime = serviceProvider.GetRequiredService<IActorRuntimeAdapter>();
```

### Runtime-Specific Registration

```csharp
// Explicit InMemory runtime
services.AddAgctorInMemory();

// Orleans runtime (placeholder)
services.AddAgctorOrleans();

// Proto.Actor runtime (placeholder)
services.AddAgctorProtoActor();

// Generic runtime selection
services.AddAgctor<InMemoryActorRuntime>();
```

### Using the Adapter Factory

```csharp
var factory = serviceProvider.GetRequiredService<IActorRuntimeAdapterFactory>();

// List available runtimes
foreach (var runtimeName in factory.GetAvailableRuntimes())
{
    Console.WriteLine($"Available: {runtimeName}");
}

// Create specific runtime
var inMemoryRuntime = factory.CreateRuntime("InMemory");
var orleansRuntime = factory.CreateRuntime("Orleans"); // Throws NotImplementedException

// Create with generic type
var runtime = factory.CreateRuntime<InMemoryActorRuntime>();
```

### Configuration Options

```csharp
services.AddAgctor(options =>
{
    options.DefaultRuntime = "InMemory";           // Default runtime to use
    options.MaxConcurrentMessages = 1000;         // Max concurrent message processing
    options.DefaultTimeoutMs = 30000;             // Default request timeout
    options.EnableDetailedLogging = true;         // Enable detailed logging
    options.Environment = "Development";          // Environment name
    options.AdditionalProperties["CustomKey"] = "CustomValue"; // Runtime-specific config
});
```

## File Structure

```
AgctorSDK.Core/
├── Interfaces/
│   ├── IActorRuntimeAdapter.cs          # Main adapter interface
│   └── IActorRuntimeAdapterFactory.cs   # Factory interface
├── Adapters/
│   ├── OrleansAdapter.cs                # Orleans placeholder adapter
│   └── ProtoActorAdapter.cs             # Proto.Actor placeholder adapter
├── Runtime/
│   └── InMemoryActorRuntime.cs          # Working in-memory implementation
├── DependencyInjection/
│   ├── ServiceCollectionExtensions.cs   # DI registration extensions
│   └── ActorRuntimeAdapterFactory.cs    # Factory implementation
└── Program.cs                           # Demo application
```

## Demo Applications

### Core SDK Demo (`AgctorSDK.Core`)

Run the core demo to see all adapter pattern features:

```bash
cd AgctorSDK.Core
dotnet run
```

**Available demos:**
1. **Basic Demo** - Direct InMemory runtime usage (legacy approach)
2. **Adapter Pattern Demo** - Shows all available adapters and their status
3. **Dependency Injection Demo** - Demonstrates different DI registration methods
4. **Runtime Switching Demo** - Shows dynamic runtime creation and switching
5. **Comprehensive Demo** - Full feature demonstration
6. **Performance Test** - Performance benchmarking

### CLI Demo (`AgctorCLI`)

Run the CLI demo with different runtime parameters:

```bash
cd AgctorCLI

# Use default InMemory runtime
dotnet run

# Specify runtime explicitly
dotnet run InMemory
dotnet run Orleans      # Will show placeholder behavior
dotnet run Proto.Actor  # Will show placeholder behavior
```

## Implementation Details

### Adapter Interface

The `IActorRuntimeAdapter` interface provides a comprehensive contract for actor runtime implementations:

```csharp
public interface IActorRuntimeAdapter : IDisposable
{
    string Name { get; }
    string Version { get; }
    bool IsInitialized { get; }
    IReadOnlyDictionary<string, object> Configuration { get; }

    Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor;
    Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor;
    Task SendMessageAsync(string targetActorId, object message, string? senderId = null, IDictionary<string, object>? headers = null, CancellationToken cancellationToken = default);
    Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, object>? headers = null, CancellationToken cancellationToken = default) where TResponse : class;
    Task StopActorAsync(string actorId, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default);
    Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    event EventHandler<ActorSpawnedEventArgs>? ActorSpawned;
    event EventHandler<ActorStoppedEventArgs>? ActorStopped;
    event EventHandler<MessageSentEventArgs>? MessageSent;
}
```

### Placeholder Adapters

The Orleans and Proto.Actor adapters are currently placeholders that throw `NotImplementedException` for all operations. Each method includes detailed TODO comments explaining what the actual implementation should do:

```csharp
public Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default)
{
    throw new NotImplementedException("Orleans adapter initialization is not yet implemented. " +
        "This will include Orleans silo host setup, grain registration, and cluster configuration.");
}
```

### Factory Pattern

The adapter factory enables runtime selection and dynamic adapter creation:

```csharp
public class ActorRuntimeAdapterFactory : IActorRuntimeAdapterFactory
{
    private static readonly Dictionary<string, Type> RuntimeTypeMap = new()
    {
        { "InMemory", typeof(InMemoryActorRuntime) },
        { "Orleans", typeof(OrleansAdapter) },
        { "Proto.Actor", typeof(ProtoActorAdapter) }
    };

    public IActorRuntimeAdapter CreateRuntime(string runtimeName) { /* ... */ }
    public T CreateRuntime<T>() where T : class, IActorRuntimeAdapter { /* ... */ }
    // ... other methods
}
```

## Benefits

1. **Pluggable Architecture** - Easy to swap runtime backends without changing application code
2. **Consistent API** - Same interface regardless of underlying runtime
3. **Dependency Injection** - Full DI support for configuration and lifecycle management
4. **Hot-Swappable** - Runtime adapters can be changed at runtime using the factory
5. **Extensible** - New runtime adapters can be added by implementing the interface
6. **Configuration-Driven** - Runtime selection can be controlled through configuration
7. **Testing-Friendly** - Easy to mock or substitute runtimes for testing

## Future Development

### Orleans Adapter Implementation

The Orleans adapter will include:
- Silo host initialization and configuration
- Grain registration and activation
- Cluster membership and discovery
- Orleans-specific message routing
- Grain lifecycle management
- Orleans statistics and monitoring

### Proto.Actor Adapter Implementation

The Proto.Actor adapter will include:
- Actor system initialization
- Props-based actor spawning
- PID management and resolution
- Proto.Actor message routing
- Cluster configuration
- Proto.Actor metrics collection

### Additional Adapters

Potential future adapters:
- **Akka.NET** - Integration with Akka.NET actor framework
- **wasmCloud** - WebAssembly-based distributed actor runtime
- **Dapr** - Dapr actors integration
- **Service Fabric** - Azure Service Fabric reliable actors

## Testing

The adapter pattern includes comprehensive testing capabilities:

1. **Unit Tests** - Test individual adapter implementations
2. **Integration Tests** - Test adapter factory and DI registration
3. **Placeholder Tests** - Verify placeholder adapters throw appropriate exceptions
4. **Configuration Tests** - Test various configuration scenarios
5. **Runtime Switching Tests** - Test dynamic runtime switching

## Contributing

When implementing new runtime adapters:

1. Implement the `IActorRuntimeAdapter` interface
2. Add the adapter to the `RuntimeTypeMap` in `ActorRuntimeAdapterFactory`
3. Create a corresponding extension method in `ServiceCollectionExtensions`
4. Add comprehensive unit and integration tests
5. Update documentation and examples

## Conclusion

The Agctor SDK adapter pattern provides a robust, extensible foundation for supporting multiple actor runtime backends. The current implementation demonstrates the pattern with a fully functional InMemory runtime and placeholder adapters for Orleans and Proto.Actor, setting the stage for future runtime integrations. 