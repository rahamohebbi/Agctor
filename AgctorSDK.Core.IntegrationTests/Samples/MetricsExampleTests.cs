using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Xunit;
using Xunit.Abstractions;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Utils.Observability.Metrics;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Interfaces;
using System.Threading;
using AgctorSDK.Core.Adapters;

namespace AgctorSDK.Core.IntegrationTests.Samples
{
    /// <summary>
    /// Integration test class that demonstrates how to use the metrics collection system.
    /// </summary>
    public class MetricsExampleTests
    {
        private readonly ITestOutputHelper _output;
        
        public MetricsExampleTests(ITestOutputHelper output)
        {
            _output = output;
        }
        
        [Fact]
        public async Task MetricsCollectionExample()
        {
            // Set up services with metrics enabled
            var services = new ServiceCollection();
            
            // Add OpenTelemetry with console exporter for metrics
            services.AddOpenTelemetry()
                .WithMetrics(builder => builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("AgctorMetricsExample"))
                    .AddMeter("AgctorSDK.Core") // Match the meter name used in OpenTelemetryMetricsCollector
                    .AddConsoleExporter());
            
            // Add Agctor with metrics enabled
            // For this example, we'll just register the metrics services
            services.AddAgctorMetrics();
            
            // Build the service provider
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the metrics collector for manual metrics
            var metricsCollector = serviceProvider.GetRequiredService<IMetricsCollector>();
            
            // Record some custom metrics
            metricsCollector.IncrementCounter(
                "agctor_custom_operation_count", 
                1, 
                new KeyValuePair<string, object>("operation_type", "sample_test"));
                
            // Measure an operation duration
            using (var timer = metricsCollector.TimeOperation(
                "agctor_custom_operation_duration_ms",
                new KeyValuePair<string, object>("operation_type", "example_operation")))
            {
                // Simulate some work
                await Task.Delay(50);
            }
            
            // In a real application with the actor runtime, we would:
            // 1. Register an actor runtime adapter
            // 2. Decorate it with MetricsEnabledActorRuntimeAdapter
            // 3. Create actors and send messages to generate metrics
            
            _output.WriteLine("Manual metrics recorded successfully");
            
            // Create a test actor to demonstrate actor metrics
            var mockActor = new TestActor("test-actor-id");
            var metricsActor = new MetricsEnabledActor(mockActor, metricsCollector);
            
            // Send a message to the actor to generate metrics
            var envelope = new MessageEnvelope(new TestMessage { Content = "Hello, Metrics!" });
            await metricsActor.ReceiveAsync(envelope);
            
            // Clean up the actor
            await metricsActor.ShutdownAsync();
            
            _output.WriteLine("Actor metrics recorded successfully");
            
            // Wait for metrics to be processed and exported (in a real app, you wouldn't need this)
            await Task.Delay(100);
            
            _output.WriteLine("Metrics collection example completed.");
            // In a real application, you would see metrics in your OpenTelemetry backend
        }
        
        /// <summary>
        /// Simple test actor for the metrics example.
        /// </summary>
        private class TestActor : IActor
        {
            public string Id { get; }
            public string ActorType => "TestActor";
            public ActorState State { get; private set; }
            
            public event EventHandler<ActorStateChangedEventArgs>? StateChanged;
            
            public TestActor(string id)
            {
                Id = id;
                State = ActorState.Initializing;
            }
            
            public Task InitializeAsync(CancellationToken cancellationToken = default)
            {
                State = ActorState.Active;
                StateChanged?.Invoke(this, new ActorStateChangedEventArgs(ActorState.Initializing, ActorState.Active, "Initialized"));
                return Task.CompletedTask;
            }
            
            public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
            {
                // Simulate some work
                Task.Delay(25, cancellationToken).GetAwaiter().GetResult();
                
                // Create a response envelope
                var response = new MessageEnvelope("Response to: " + envelope.Payload);
                return Task.FromResult<IMessageEnvelope>(response);
            }
            
            public Task ShutdownAsync(CancellationToken cancellationToken = default)
            {
                State = ActorState.Stopped;
                StateChanged?.Invoke(this, new ActorStateChangedEventArgs(ActorState.Active, ActorState.Stopped, "Shut down"));
                return Task.CompletedTask;
            }
        }
        
        /// <summary>
        /// Simple test message for the metrics example.
        /// </summary>
        private class TestMessage
        {
            public string Content { get; set; } = string.Empty;
        }
    }
} 