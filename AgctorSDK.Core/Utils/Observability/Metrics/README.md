# Agctor Metrics Collection

This module provides metrics collection for the Agctor system using OpenTelemetry.

## Overview

The metrics collection system in Agctor provides:

1. **Abstracted Metrics Interface**: A clean, pluggable metrics API that can work with different backend implementations
2. **OpenTelemetry Integration**: Built-in support for OpenTelemetry metrics collection
3. **Actor Performance Metrics**: Automatic collection of message processing times, actor counts, and more
4. **Runtime Metrics**: Tracking of message throughput, queue depths, and runtime performance
5. **Custom Metrics Support**: Easy API for applications to record their own metrics

## Core System Metrics

The following core metrics are automatically collected:

### Message Throughput Metrics
- `agctor_core_messages_processed_total`: Counter for processed messages (with tags for actor type, message type, status)
- `agctor_core_message_processing_time_ms`: Histogram of message processing times
- `agctor_core_message_queue_depth`: Gauge for message queue depth
- `agctor_core_message_size_bytes`: Histogram of message sizes

### Actor Count Metrics
- `agctor_core_actors_created_total`: Counter for actors created
- `agctor_core_actors_destroyed_total`: Counter for actors destroyed
- `agctor_core_active_actors`: Gauge for currently active actors
- `agctor_core_actors_by_type`: Gauge showing counts by actor type

### Runtime Metrics
- `agctor_runtime_actor_creation_time_ms`: Histogram of actor creation times
- `agctor_runtime_actor_deactivation_time_ms`: Histogram of actor deactivation times
- `agctor_runtime_message_delivery_time_ms`: Histogram of message delivery times
- `agctor_runtime_message_delivery_total`: Counter for message delivery attempts (success/failure)
- `agctor_runtime_timed_message_delivery_ms`: Histogram of timed message delivery durations
- `agctor_runtime_timed_message_delivery_total`: Counter for timed message delivery attempts (success/timeout)

## Usage

### Enabling Metrics in Your Application

Use the extension methods in `ServiceCollectionExtensions` to enable metrics:

```csharp
// Add Agctor with metrics enabled using the default runtime
services.AddAgctorWithMetrics();

// Or with a specific runtime
services.AddAgctorWithMetrics<InMemoryActorRuntime>();
```

### Configure OpenTelemetry Exporters

Configure OpenTelemetry exporters to send metrics to your monitoring system:

```csharp
services.AddOpenTelemetry()
    .WithMetrics(builder => builder
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("YourServiceName"))
        .AddMeter("AgctorSDK.Core")
        .AddPrometheusExporter()  // or other exporters like OTLP
    );
```

### Recording Custom Metrics

Inject `IMetricsCollector` to record custom metrics:

```csharp
public class YourService
{
    private readonly IMetricsCollector _metrics;
    
    public YourService(IMetricsCollector metrics)
    {
        _metrics = metrics;
    }
    
    public void PerformOperation()
    {
        // Increment a counter
        _metrics.IncrementCounter(
            "your_operation_count", 
            1, 
            new KeyValuePair<string, object>("operation_type", "example"));
            
        // Record a gauge value
        _metrics.RecordGauge(
            "your_resource_usage", 
            42.5, 
            new KeyValuePair<string, object>("resource", "memory"));
            
        // Time an operation
        using var timer = _metrics.TimeOperation(
            "your_operation_duration_ms",
            new KeyValuePair<string, object>("operation", "calculation"));
            
        // Do work...
    }
}
```

## Implementation Details

The metrics system uses the decorator pattern to add metrics collection to existing components:

- `MetricsEnabledActor`: Decorates `IActor` to collect actor-level metrics
- `MetricsEnabledActorRuntimeAdapter`: Decorates `IActorRuntimeAdapter` to collect runtime metrics

## Disabling Metrics

If you need to disable metrics in certain environments, use the no-op implementation:

```csharp
services.AddAgctor(); // Standard Agctor setup without metrics
services.AddAgctorNoOpMetrics(); // Add no-op metrics collection
```

## Best Practices

1. **Use Consistent Naming**: Follow the `namespace_component_metric_unit` pattern
2. **Add Descriptive Tags**: Include tags for better filtering and aggregation
3. **Choose Appropriate Metric Types**:
   - Counters for things that increase (e.g., messages processed)
   - Gauges for current values (e.g., active actors)
   - Histograms for distributions (e.g., processing times)
4. **Record Business Metrics**: Add domain-specific metrics that are relevant to your application

## Integration with Prometheus

Agctor metrics work well with Prometheus. Add the Prometheus exporter:

```csharp
services.AddOpenTelemetry()
    .WithMetrics(builder => builder
        .AddMeter("AgctorSDK.Core")
        .AddPrometheusExporter());
```

Then add the Prometheus scrape endpoint to your application:

```csharp
app.UseOpenTelemetryPrometheusScrapingEndpoint();
```

## Future Enhancements

Planned enhancements to the metrics system:

1. More detailed actor state metrics
2. Memory usage tracking per actor
3. Tool usage metrics
4. LLM integration metrics (token usage, costs)
5. Better queue depth tracking 