using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.Utils.Observability.Metrics
{
    /// <summary>
    /// Decorator for IActor that collects metrics about message processing and actor lifecycle.
    /// </summary>
    public class MetricsEnabledActor : IActor
    {
        private readonly IActor _innerActor;
        private readonly IMetricsCollector _metricsCollector;
        private readonly string _actorType;
        
        /// <summary>
        /// Creates a new metrics-enabled actor decorator.
        /// </summary>
        /// <param name="innerActor">The actor to decorate</param>
        /// <param name="metricsCollector">The metrics collector to use</param>
        public MetricsEnabledActor(IActor innerActor, IMetricsCollector metricsCollector)
        {
            _innerActor = innerActor;
            _metricsCollector = metricsCollector;
            _actorType = innerActor.GetType().Name;
            
            // Record actor creation
            _metricsCollector.IncrementCounter(
                MetricsConstants.Core.ActorsCreated,
                1,
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, _actorType)
            );
            
            // Increment active actors count
            _metricsCollector.IncrementCounter(
                MetricsConstants.Core.ActiveActors,
                1,
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, _actorType)
            );
            
            // Record actors by type
            _metricsCollector.RecordGauge(
                MetricsConstants.Core.ActorsByType,
                1, // This is just incrementing by 1, ideally we'd track actual counts per type
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, _actorType)
            );
        }

        /// <inheritdoc />
        public string Id => _innerActor.Id;

        /// <inheritdoc />
        public Task ActivateAsync()
        {
            return _innerActor.ActivateAsync();
        }

        /// <inheritdoc />
        public async Task DeactivateAsync()
        {
            await _innerActor.DeactivateAsync();
            
            // Record actor destruction
            _metricsCollector.IncrementCounter(
                MetricsConstants.Core.ActorsDestroyed,
                1,
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, _actorType)
            );
            
            // Decrement active actors count
            _metricsCollector.IncrementCounter(
                MetricsConstants.Core.ActiveActors,
                -1, // Decrement by 1
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, _actorType)
            );
        }

        /// <inheritdoc />
        public async Task ProcessMessageAsync(IMessageEnvelope message)
        {
            var messageType = message.PayloadType?.Name ?? "Unknown";
            var messageSize = EstimateMessageSize(message);
            
            // Record message size
            _metricsCollector.RecordHistogram(
                MetricsConstants.Core.MessageSize,
                messageSize,
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, _actorType),
                new KeyValuePair<string, object>(MetricsConstants.Tags.MessageType, messageType)
            );
            
            // Track message processing time
            using (var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.MessageProcessingTime,
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, _actorType),
                new KeyValuePair<string, object>(MetricsConstants.Tags.MessageType, messageType)))
            {
                try
                {
                    await _innerActor.ProcessMessageAsync(message);
                    
                    // Increment successful messages counter
                    _metricsCollector.IncrementCounter(
                        MetricsConstants.Core.MessagesProcessed,
                        1,
                        new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, _actorType),
                        new KeyValuePair<string, object>(MetricsConstants.Tags.MessageType, messageType),
                        new KeyValuePair<string, object>(MetricsConstants.Tags.Status, "success")
                    );
                }
                catch (Exception)
                {
                    // Increment failed messages counter
                    _metricsCollector.IncrementCounter(
                        MetricsConstants.Core.MessagesProcessed,
                        1,
                        new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, _actorType),
                        new KeyValuePair<string, object>(MetricsConstants.Tags.MessageType, messageType),
                        new KeyValuePair<string, object>(MetricsConstants.Tags.Status, "failure")
                    );
                    
                    throw; // Re-throw the exception after recording metrics
                }
            }
        }
        
        /// <summary>
        /// Estimates the message size in bytes.
        /// </summary>
        /// <param name="message">The message to estimate the size of</param>
        /// <returns>The estimated size in bytes</returns>
        private int EstimateMessageSize(IMessageEnvelope message)
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