using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Utils.Observability.Metrics
{
    /// <summary>
    /// Decorator for IActor that collects metrics about message processing and actor lifecycle.
    /// </summary>
    public class MetricsEnabledActor : IActor
    {
        private readonly IActor _innerActor;
        private readonly IMetricsCollector _metricsCollector;
        private readonly bool _isTool;
        
        /// <summary>
        /// Creates a new metrics-enabled actor decorator.
        /// </summary>
        /// <param name="innerActor">The actor to decorate</param>
        /// <param name="metricsCollector">The metrics collector to use</param>
        public MetricsEnabledActor(IActor innerActor, IMetricsCollector metricsCollector)
        {
            _innerActor = innerActor;
            _metricsCollector = metricsCollector;
            _isTool = innerActor is AgctorSDK.Core.Tools.IToolActor;
            
            // Create common tags
            var tags = new List<KeyValuePair<string, object>>
            {
                new(MetricsConstants.Tags.ActorType, ActorType)
            };
            
            // Add tool-specific tag if this is a tool
            if (_isTool)
            {
                tags.Add(new(MetricsConstants.Tags.ActorCategory, "Tool"));
                tags.Add(new(MetricsConstants.Tags.ToolName, ActorType));
            }
            
            // Record actor creation
            _metricsCollector.IncrementCounter(
                MetricsConstants.Core.ActorsCreated,
                1,
                tags.ToArray()
            );
            
            // Increment active actors count
            _metricsCollector.IncrementCounter(
                MetricsConstants.Core.ActiveActors,
                1,
                tags.ToArray()
            );
            
            // Record actors by type
            _metricsCollector.RecordGauge(
                MetricsConstants.Core.ActorsByType,
                1, // This is just incrementing by 1, ideally we'd track actual counts per type
                tags.ToArray()
            );
        }

        /// <inheritdoc />
        public string Id => _innerActor.Id;

        /// <inheritdoc />
        public string ActorType => _innerActor.ActorType;

        /// <inheritdoc />
        public ActorState State => _innerActor.State;

        /// <inheritdoc />
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged
        {
            add => _innerActor.StateChanged += value;
            remove => _innerActor.StateChanged -= value;
        }

        /// <inheritdoc />
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var tags = new List<KeyValuePair<string, object>>
            {
                new(MetricsConstants.Tags.ActorType, ActorType)
            };
            
            if (_isTool)
            {
                tags.Add(new(MetricsConstants.Tags.ActorCategory, "Tool"));
                tags.Add(new(MetricsConstants.Tags.ToolName, ActorType));
            }
            
            using var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.ActorInitializationTime,
                tags.ToArray()
            );
            
            await _innerActor.InitializeAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            var tags = new List<KeyValuePair<string, object>>
            {
                new(MetricsConstants.Tags.ActorType, ActorType)
            };
            
            if (_isTool)
            {
                tags.Add(new(MetricsConstants.Tags.ActorCategory, "Tool"));
                tags.Add(new(MetricsConstants.Tags.ToolName, ActorType));
            }
            
            using var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.ActorShutdownTime,
                tags.ToArray()
            );
            
            await _innerActor.ShutdownAsync(cancellationToken);
            
            // Record actor destruction
            _metricsCollector.IncrementCounter(
                MetricsConstants.Core.ActorsDestroyed,
                1,
                tags.ToArray()
            );
            
            // Decrement active actors count
            _metricsCollector.IncrementCounter(
                MetricsConstants.Core.ActiveActors,
                -1, // Decrement by 1
                tags.ToArray()
            );
        }

        /// <inheritdoc />
        public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var messageType = envelope.Payload?.GetType().Name ?? "Unknown";
            var messageSize = EstimateMessageSize(envelope);
            
            // Create common tags
            var tags = new List<KeyValuePair<string, object>>
            {
                new(MetricsConstants.Tags.ActorType, ActorType),
                new(MetricsConstants.Tags.MessageType, messageType)
            };
            
            // Add tool-specific tags
            if (_isTool)
            {
                tags.Add(new(MetricsConstants.Tags.ActorCategory, "Tool"));
                tags.Add(new(MetricsConstants.Tags.ToolName, ActorType));
                
                // Check if this is a tool request to record specific tool operation
                if (envelope.Payload is ToolRequest toolRequest)
                {
                    tags.Add(new(MetricsConstants.Tags.ToolOperation, toolRequest.Operation));
                    
                    // Record tool invocation
                    _metricsCollector.IncrementCounter(
                        MetricsConstants.Tools.ToolInvocations,
                        1,
                        tags.ToArray()
                    );
                }
            }
            
            // Record message size
            _metricsCollector.RecordHistogram(
                MetricsConstants.Core.MessageSize,
                messageSize,
                tags.ToArray()
            );
            
            // Track message processing time - use tool-specific metric for tools
            string metricName = _isTool && envelope.Payload is ToolRequest
                ? MetricsConstants.Tools.ToolExecutionTime
                : MetricsConstants.Core.MessageProcessingTime;
                
            using var timer = _metricsCollector.TimeOperation(
                metricName,
                tags.ToArray()
            );
            
            try
            {
                var response = await _innerActor.ReceiveAsync(envelope, cancellationToken);
                
                // Add success status tag
                tags.Add(new(MetricsConstants.Tags.Status, "success"));
                
                // Increment successful messages counter
                _metricsCollector.IncrementCounter(
                    MetricsConstants.Core.MessagesProcessed,
                    1,
                    tags.ToArray()
                );
                
                // Track tool success if applicable
                if (_isTool && envelope.Payload is ToolRequest)
                {
                    _metricsCollector.IncrementCounter(
                        MetricsConstants.Tools.ToolSuccessRate,
                        1,
                        tags.ToArray()
                    );
                }
                
                return response;
            }
            catch (Exception)
            {
                // Add failure status tag
                tags.Add(new(MetricsConstants.Tags.Status, "failure"));
                
                // Increment failed messages counter
                _metricsCollector.IncrementCounter(
                    MetricsConstants.Core.MessagesProcessed,
                    1,
                    tags.ToArray()
                );
                
                // Track tool failure if applicable
                if (_isTool && envelope.Payload is ToolRequest)
                {
                    _metricsCollector.IncrementCounter(
                        MetricsConstants.Tools.ToolFailures,
                        1,
                        tags.ToArray()
                    );
                }
                
                throw; // Re-throw the exception after recording metrics
            }
        }
        
        /// <summary>
        /// Estimates the message size in bytes.
        /// </summary>
        /// <param name="envelope">The message envelope to estimate the size of</param>
        /// <returns>The estimated size in bytes</returns>
        private int EstimateMessageSize(IMessageEnvelope envelope)
        {
            // This is a very rough estimate
            // In a production system, you would want to use a more accurate serialization-based approach
            
            // For now, just return a constant size
            return 1024; // Assume 1KB average message size
            
            // A better implementation would actually serialize the message and measure the byte count
            // or use a more sophisticated estimation algorithm based on the message content
        }
    }
} 