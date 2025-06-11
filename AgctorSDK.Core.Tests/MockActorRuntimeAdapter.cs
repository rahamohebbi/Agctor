using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.Observability.Visualization;

namespace AgctorSDK.Core.Tests
{
    /// <summary>
    /// Mock implementation of IActorRuntimeAdapter for testing purposes.
    /// </summary>
    public class MockActorRuntimeAdapter : IActorRuntimeAdapter
    {
        public string Name => "MockActorRuntimeAdapter";
        
        public string Version => "1.0.0";
        
        public bool IsInitialized => true;
        
        public IReadOnlyDictionary<string, object> Configuration => new Dictionary<string, object>();
        
        public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned;
        
        public event EventHandler<ActorStoppedEventArgs>? ActorStopped;
        
        public event EventHandler<MessageSentEventArgs>? MessageSent;

        /// <inheritdoc />
        public Task<TResponse> CallActorAsync<TResponse>(string actorId, object message, CancellationToken cancellationToken = default)
        {
            // Just return default value
            return Task.FromResult(default(TResponse)!);
        }
        
        public Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }
        
        public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor
        {
            return Task.FromResult<T?>(null);
        }
        
        public Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IRuntimeStatistics>(new TestRuntimeStatistics());
        }
        
        public Task InitializeAsync(IDictionary<string, object>? configuration = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RegisterActorAsync(IActor actor, CancellationToken cancellationToken = default)
        {
            // Do nothing
            return Task.CompletedTask;
        }
        
        public Task<string> RequestHumanInputAsync(string prompt, string actorId, string conversationId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("Mock human input");
        }

        /// <inheritdoc />
        public Task SendActorMessageAsync(string actorId, object message, CancellationToken cancellationToken = default)
        {
            // Do nothing
            return Task.CompletedTask;
        }
        
        public Task SendMessageAsync(string actorId, object message, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        
        public Task<TResponse> SendMessageAsync<TResponse>(string actorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) where TResponse : class
        {
            return Task.FromResult(default(TResponse)!);
        }
        
        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        
        public Task<T> SpawnActorAsync<T>(string actorId, object? state = null, CancellationToken cancellationToken = default) where T : class, IActor
        {
            return Task.FromResult(Activator.CreateInstance<T>());
        }
        
        public Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> factory, object? state = null, CancellationToken cancellationToken = default) where T : class, IActor
        {
            return Task.FromResult(factory(actorId));
        }

        /// <inheritdoc />
        public Task StopActorAsync(string actorId, CancellationToken cancellationToken = default)
        {
            // Do nothing
            return Task.CompletedTask;
        }
        
        public void Dispose()
        {
            // Nothing to dispose
        }
    }
    
    /// <summary>
    /// Simple implementation of runtime statistics for test purposes.
    /// </summary>
    public class TestRuntimeStatistics : IRuntimeStatistics
    {
        public int ActiveActorCount => 0;
        
        public long MessagesProcessed => 0;
        
        public TimeSpan Uptime => TimeSpan.FromSeconds(0);
        
        public IDictionary<string, object> AdditionalStats => new Dictionary<string, object>();

        public long TotalMessagesProcessed => 0;

        public double MessagesPerSecond => 0;

        public double AverageMessageProcessingTime => 0;

        public long MemoryUsageBytes => 0;

        public IReadOnlyDictionary<string, object> AdditionalMetrics => new Dictionary<string, object>();
    }
} 