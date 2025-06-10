# Agctor Activity Tracking System

This directory contains the implementation of the Agctor Activity Tracking System, which provides observability for agent operations. The system is designed to be flexible, extensible, and to work with different tracing backends.

## Key Components

- `IActivityTracker`: Core abstraction for tracking activities across agents
- `IActivityScope`: Represents a single traceable activity/span
- `ActivityStatus`: Enum for representing the status of an activity

## Implementations

### Logger-Based Tracking

The `LoggerActivityTracker` provides a simple implementation that logs activities using the existing Agctor logging system. This is useful for basic tracing without external dependencies.

### OpenTelemetry-Based Tracking

The `OpenTelemetryActivityTracker` provides a more advanced implementation that integrates with the OpenTelemetry tracing system. This enables distributed tracing, visualization in tools like Zipkin, and more detailed analysis.

## Usage Examples

### Basic Usage

```csharp
// Get the activity tracker from the dependency injection container
var activityTracker = serviceProvider.GetRequiredService<IActivityTracker>();

// Start a new activity
using (var activity = activityTracker.StartActivity("Process Request"))
{
    // Add attributes to the activity
    activity.SetAttribute("request.id", "12345");
    activity.SetAttribute("request.type", "GET");
    
    try
    {
        // Perform some work
        ProcessRequest();
        
        // Record an event
        activity.RecordEvent("Request Processed", new Dictionary<string, object>
        {
            { "processing_time_ms", 42 },
            { "cache_hit", true }
        });
        
        // Set the status to OK
        activity.SetStatus(ActivityStatus.Ok);
    }
    catch (Exception ex)
    {
        // Record the exception
        activity.RecordException(ex);
        
        // Set the status to Error
        activity.SetStatus(ActivityStatus.Error, ex.Message);
        
        // Rethrow or handle as needed
        throw;
    }
}
```

### Propagating Context Between Agents

```csharp
// In the sending agent
public async Task SendMessageToChildAgent(IActorRuntimeAdapter runtime, string targetActorId, object message)
{
    using (var activity = _activityTracker.StartActivity("SendMessage"))
    {
        activity.SetAttribute("target_actor", targetActorId);
        
        // Create a message envelope
        var envelope = new MessageEnvelope(message);
        
        // Propagate the activity context to the message envelope
        // This returns a new envelope with the context headers
        var envelopeWithContext = envelope.PropagateActivityContext(_activityTracker);
        
        // Send the message with the context
        await runtime.SendMessageAsync(targetActorId, envelopeWithContext);
        
        activity.RecordEvent("MessageSent");
    }
}

// In the receiving agent
public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken)
{
    // Extract the activity context from the message envelope
    var context = envelope.ExtractActivityContext();
    
    // Start a new activity with the parent context
    using (var activity = _activityTracker.StartActivity("ProcessMessage", context))
    {
        activity.SetAttribute("message_type", envelope.Payload.GetType().Name);
        
        try
        {
            // Process the message
            var result = await ProcessMessageAsync(envelope.Payload, cancellationToken);
            
            // Create a response envelope
            var responseEnvelope = new MessageEnvelope(result);
            
            // Propagate the activity context to the response
            return responseEnvelope.PropagateActivityContext(_activityTracker);
        }
        catch (Exception ex)
        {
            activity.RecordException(ex);
            activity.SetStatus(ActivityStatus.Error, ex.Message);
            throw;
        }
    }
}
```

### Registering Activity Tracking in Dependency Injection

```csharp
// In your startup code
services.AddAgctorActivityTracking(options =>
{
    options.EnableToolTracing = true;
});

// Or for OpenTelemetry-based tracking
services.AddAgctorOpenTelemetryTracking(options =>
{
    options.SourceName = "MyApplication";
    options.EnableZipkinExporter = true;
    options.ZipkinEndpoint = "http://localhost:9411/api/v2/spans";
});
```

## Best Practices

1. Use descriptive activity names that clearly indicate what operation is being performed
2. Add relevant attributes to activities to provide context
3. Record events to mark important milestones within an activity
4. Always set the activity status before completing it
5. Record exceptions when they occur
6. Use the `using` statement with activities to ensure they are properly disposed

## Demo

The `ActivityTrackingDemo` class provides a complete demonstration of how to use the activity tracking system with both the logger-based and OpenTelemetry-based implementations.

To run the demo:

```csharp
await ActivityTrackingDemo.RunActivityTrackingDemoAsync();
``` 