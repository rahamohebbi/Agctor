using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Runtime;

namespace AgctorSDK.Core.Utils.Observability.Metrics
{
    /// <summary>
    /// Decorator for IActorRuntimeAdapter that collects metrics about the actor runtime.
    /// </summary>
    public class MetricsEnabledActorRuntimeAdapter : IActorRuntimeAdapter
    {
        private readonly IActorRuntimeAdapter _innerAdapter;
        private readonly IMetricsCollector _metricsCollector;
        private readonly string _runtimeType;
        
        /// <summary>
        /// Creates a new metrics-enabled actor runtime adapter decorator.
        /// </summary>
        /// <param name="innerAdapter">The runtime adapter to decorate</param>
        /// <param name="metricsCollector">The metrics collector to use</param>
        public MetricsEnabledActorRuntimeAdapter(
            IActorRuntimeAdapter innerAdapter,
            IMetricsCollector metricsCollector)
        {
            _innerAdapter = innerAdapter;
            _metricsCollector = metricsCollector;
            _runtimeType = innerAdapter.GetType().Name;
        }
        
        /// <inheritdoc />
        public async Task<IActor> CreateActorAsync(Type actorType, params object[] args)
        {
            // Start timing actor creation
            using var timer = _metricsCollector.TimeOperation(
                "agctor_runtime_actor_creation_time_ms",
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, _runtimeType),
                new KeyValuePair<string, object>("actor_class", actorType.Name)
            );
            
            // Create the actor
            var actor = await _innerAdapter.CreateActorAsync(actorType, args);
            
            // Wrap the actor with metrics
            return new MetricsEnabledActor(actor, _metricsCollector);
        }

        /// <inheritdoc />
        public Task<IActor> GetActorAsync(string actorId)
        {
            return _innerAdapter.GetActorAsync(actorId);
        }

        /// <inheritdoc />
        public async Task<bool> DeactivateActorAsync(string actorId)
        {
            // Start timing actor deactivation
            using var timer = _metricsCollector.TimeOperation(
                "agctor_runtime_actor_deactivation_time_ms",
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, _runtimeType)
            );
            
            return await _innerAdapter.DeactivateActorAsync(actorId);
        }

        /// <inheritdoc />
        public async Task<bool> DeliverMessageAsync(string targetActorId, IMessageEnvelope message)
        {
            // Track queue depth (this would ideally be pulled from the actual queue)
            // For now, we just record a constant value
            _metricsCollector.RecordGauge(
                MetricsConstants.Core.MessageQueueDepth,
                1, // This would be the actual queue depth in a real implementation
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, _runtimeType),
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorId, targetActorId)
            );
            
            // Start timing message delivery (queue time + processing time)
            using var timer = _metricsCollector.TimeOperation(
                "agctor_runtime_message_delivery_time_ms",
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, _runtimeType),
                new KeyValuePair<string, object>(MetricsConstants.Tags.MessageType, message.PayloadType?.Name ?? "Unknown")
            );
            
            var result = await _innerAdapter.DeliverMessageAsync(targetActorId, message);
            
            // Record delivery result
            _metricsCollector.IncrementCounter(
                "agctor_runtime_message_delivery_total",
                1,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, _runtimeType),
                new KeyValuePair<string, object>(MetricsConstants.Tags.Status, result ? "success" : "failure")
            );
            
            return result;
        }

        /// <inheritdoc />
        public async Task<bool> TryDeliverMessageAsync(string targetActorId, IMessageEnvelope message, TimeSpan timeout)
        {
            // Start timing message delivery with timeout
            using var timer = _metricsCollector.TimeOperation(
                "agctor_runtime_timed_message_delivery_ms",
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, _runtimeType),
                new KeyValuePair<string, object>(MetricsConstants.Tags.MessageType, message.PayloadType?.Name ?? "Unknown"),
                new KeyValuePair<string, object>("timeout_ms", timeout.TotalMilliseconds.ToString("F0"))
            );
            
            var result = await _innerAdapter.TryDeliverMessageAsync(targetActorId, message, timeout);
            
            // Record delivery result
            _metricsCollector.IncrementCounter(
                "agctor_runtime_timed_message_delivery_total",
                1,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, _runtimeType),
                new KeyValuePair<string, object>(MetricsConstants.Tags.Status, result ? "success" : "timeout")
            );
            
            return result;
        }
    }
} 