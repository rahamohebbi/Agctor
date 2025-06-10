using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.Utils.Observability.Metrics
{
    /// <summary>
    /// Decorator for IActorRuntimeAdapter that collects metrics about the actor runtime.
    /// </summary>
    public class MetricsEnabledActorRuntimeAdapter : IActorRuntimeAdapter
    {
        private readonly IActorRuntimeAdapter _innerAdapter;
        private readonly IMetricsCollector _metricsCollector;
        
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
        }

        /// <inheritdoc />
        public string Name => _innerAdapter.Name;

        /// <inheritdoc />
        public string Version => _innerAdapter.Version;

        /// <inheritdoc />
        public bool IsInitialized => _innerAdapter.IsInitialized;

        /// <inheritdoc />
        public IReadOnlyDictionary<string, object> Configuration => _innerAdapter.Configuration;

        /// <inheritdoc />
        public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned
        {
            add => _innerAdapter.ActorSpawned += value;
            remove => _innerAdapter.ActorSpawned -= value;
        }

        /// <inheritdoc />
        public event EventHandler<ActorStoppedEventArgs>? ActorStopped
        {
            add => _innerAdapter.ActorStopped += value;
            remove => _innerAdapter.ActorStopped -= value;
        }

        /// <inheritdoc />
        public event EventHandler<MessageSentEventArgs>? MessageSent
        {
            add => _innerAdapter.MessageSent += value;
            remove => _innerAdapter.MessageSent -= value;
        }

        /// <inheritdoc />
        public async Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default)
        {
            using var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.RuntimeInitializationTime,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name)
            );
            
            await _innerAdapter.InitializeAsync(configuration, cancellationToken);
        }

        /// <inheritdoc />
        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            using var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.RuntimeShutdownTime,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name)
            );
            
            await _innerAdapter.ShutdownAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
        {
            using var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.ActorCreationTime,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, typeof(T).Name)
            );
            
            var actor = await _innerAdapter.SpawnActorAsync<T>(actorId, initializationData, cancellationToken);
            
            // Wrap the actor with metrics if it's not already wrapped
            if (actor is not null && actor is not MetricsEnabledActor)
            {
                var wrappedActor = new MetricsEnabledActor(actor, _metricsCollector) as T;
                if (wrappedActor is not null)
                {
                    return wrappedActor;
                }
            }
            
            return actor;
        }

        /// <inheritdoc />
        public async Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> actorFactory, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
        {
            using var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.ActorCreationTime,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, typeof(T).Name)
            );
            
            var actor = await _innerAdapter.SpawnActorAsync<T>(actorId, actorFactory, initializationData, cancellationToken);
            
            // Wrap the actor with metrics if it's not already wrapped
            if (actor is not null && actor is not MetricsEnabledActor)
            {
                var wrappedActor = new MetricsEnabledActor(actor, _metricsCollector) as T;
                if (wrappedActor is not null)
                {
                    return wrappedActor;
                }
            }
            
            return actor;
        }

        /// <inheritdoc />
        public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor
        {
            _metricsCollector.IncrementCounter(
                MetricsConstants.Core.ActorLookups,
                1,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorType, typeof(T).Name)
            );
            
            return _innerAdapter.GetActorAsync<T>(actorId, cancellationToken);
        }

        /// <inheritdoc />
        public async Task SendMessageAsync(string targetActorId, object message, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            // Track queue depth (this would ideally be pulled from the actual queue)
            // For now, we just record a constant value
            _metricsCollector.RecordGauge(
                MetricsConstants.Core.MessageQueueDepth,
                1, // This would be the actual queue depth in a real implementation
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorId, targetActorId)
            );
            
            // Start timing message delivery (queue time + processing time)
            using var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.MessageDeliveryTime,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                new KeyValuePair<string, object>(MetricsConstants.Tags.MessageType, message.GetType().Name)
            );
            
            try
            {
                await _innerAdapter.SendMessageAsync(targetActorId, message, senderId, headers, cancellationToken);
                
                // Record delivery result
                _metricsCollector.IncrementCounter(
                    MetricsConstants.Core.MessagesDelivered,
                    1,
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Status, "success")
                );
            }
            catch (Exception)
            {
                // Record delivery failure
                _metricsCollector.IncrementCounter(
                    MetricsConstants.Core.MessagesDelivered,
                    1,
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Status, "failure")
                );
                
                throw; // Re-throw the exception after recording metrics
            }
        }

        /// <inheritdoc />
        public async Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
            where TResponse : class
        {
            // Start timing message delivery with timeout
            using var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.MessageRoundtripTime,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                new KeyValuePair<string, object>(MetricsConstants.Tags.MessageType, message.GetType().Name),
                new KeyValuePair<string, object>("timeout_ms", timeout.TotalMilliseconds.ToString("F0"))
            );
            
            try
            {
                var response = await _innerAdapter.SendMessageAsync<TResponse>(targetActorId, message, timeout, senderId, headers, cancellationToken);
                
                // Record delivery result
                _metricsCollector.IncrementCounter(
                    MetricsConstants.Core.MessagesWithResponse,
                    1,
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Status, "success")
                );
                
                return response;
            }
            catch (TimeoutException)
            {
                // Record timeout
                _metricsCollector.IncrementCounter(
                    MetricsConstants.Core.MessagesWithResponse,
                    1,
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Status, "timeout")
                );
                
                throw;
            }
            catch (Exception)
            {
                // Record other failures
                _metricsCollector.IncrementCounter(
                    MetricsConstants.Core.MessagesWithResponse,
                    1,
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                    new KeyValuePair<string, object>(MetricsConstants.Tags.Status, "failure")
                );
                
                throw;
            }
        }

        /// <inheritdoc />
        public Task StopActorAsync(string actorId, CancellationToken cancellationToken = default)
        {
            using var timer = _metricsCollector.TimeOperation(
                MetricsConstants.Core.ActorStopTime,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name)
            );
            
            return _innerAdapter.StopActorAsync(actorId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default)
        {
            return _innerAdapter.GetActiveActorIdsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            // We could enhance the statistics with additional metrics from our collector here
            return _innerAdapter.GetStatisticsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<string> RequestHumanInputAsync(string prompt, string title, string actorId, CancellationToken cancellationToken = default)
        {
            _metricsCollector.IncrementCounter(
                "agctor_runtime_human_input_requests",
                1,
                new KeyValuePair<string, object>(MetricsConstants.Tags.Runtime, Name),
                new KeyValuePair<string, object>(MetricsConstants.Tags.ActorId, actorId)
            );
            
            return _innerAdapter.RequestHumanInputAsync(prompt, title, actorId, cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _innerAdapter.Dispose();
            GC.SuppressFinalize(this);
        }
    }
} 