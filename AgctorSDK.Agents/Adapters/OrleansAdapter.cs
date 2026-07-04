using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Orleans.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;

namespace AgctorSDK.Core.Adapters
{
    /// <summary>
    /// <strong>Experimental.</strong> Orleans client adapter — connects to a silo (local Docker or remote cluster).
    /// Enable via <c>Agctor:AllowExperimentalRuntimes=true</c>.
    /// </summary>
    public class OrleansAdapter : IActorRuntimeAdapter
    {
        private bool _isDisposed;
        private bool _isInitialized;
        private readonly Dictionary<string, object> _configuration = new();
        private IHost? _clientHost;
        private IClusterClient? _clusterClient;
        private DateTimeOffset _startTime;
        private readonly ConcurrentDictionary<string, string> _knownGrainIds = new();

        public string Name => "Orleans";
        public string Version => "1.0.0-client";
        public bool IsInitialized => _isInitialized;
        public IReadOnlyDictionary<string, object> Configuration => _configuration;

#pragma warning disable CS0067
        public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned;
        public event EventHandler<ActorStoppedEventArgs>? ActorStopped;
        public event EventHandler<MessageSentEventArgs>? MessageSent;
        public event EventHandler<DeadLetterEventArgs>? DeadLetter;
#pragma warning restore CS0067

        /// <summary>
        /// Connects an Orleans client to the configured gateway and verifies cluster health.
        /// </summary>
        public async Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default)
        {
            if (_isInitialized) return;
            foreach (var kvp in configuration) _configuration[kvp.Key] = kvp.Value;

            var clusterId = GetConfigString("clusterId", "agctor-dev");
            var serviceId = GetConfigString("serviceId", "agctor-host");
            var gatewayPort = GetConfigInt("gatewayPort", 30000);

            _clientHost = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddOrleansClient(clientBuilder =>
                    {
                        clientBuilder
                            .Configure<ClusterOptions>(options =>
                            {
                                options.ClusterId = clusterId;
                                options.ServiceId = serviceId;
                            })
                            .UseLocalhostClustering();
                    });
                })
                .Build();

            await _clientHost.StartAsync(cancellationToken).ConfigureAwait(false);
            _clusterClient = _clientHost.Services.GetRequiredService<IClusterClient>();

            var ping = await _clusterClient.GetGrain<IAgctorHealthGrain>(0).PingAsync().ConfigureAwait(false);
            if (!string.Equals(ping, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Orleans health grain returned unexpected value: {ping}");

            _startTime = DateTimeOffset.UtcNow;
            _isInitialized = true;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            if (!_isInitialized) return;
            _knownGrainIds.Clear();
            if (_clientHost != null)
            {
                await _clientHost.StopAsync(cancellationToken).ConfigureAwait(false);
                _clientHost.Dispose();
                _clientHost = null;
            }

            _clusterClient = null;
            _isInitialized = false;
        }

        public Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
            => throw new NotImplementedException("Orleans grain bridge for IActor spawn is not implemented yet. Use InMemory for local agent tests.");

        public Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> actorFactory, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
            => throw new NotImplementedException("Orleans grain bridge for IActor spawn is not implemented yet.");

        public Task RegisterActorAsync(IActor actor, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Orleans RegisterActorAsync is not implemented yet.");

        public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor
            => throw new NotImplementedException("Orleans GetActorAsync is not implemented yet.");

        public Task SendMessageAsync(string targetActorId, object message, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Orleans SendMessageAsync is not implemented yet.");

        public Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) where TResponse : class
            => throw new NotImplementedException("Orleans request-response is not implemented yet.");

        public Task StopActorAsync(string actorId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Orleans StopActorAsync is not implemented yet.");

        public Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<string>>(_knownGrainIds.Keys);

        public Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            if (!_isInitialized || _clusterClient == null)
                throw new InvalidOperationException("Orleans runtime is not initialized.");

            var stats = new OrleansRuntimeStatistics
            {
                ActiveActorCount = _knownGrainIds.Count,
                TotalMessagesProcessed = 0,
                MessagesPerSecond = 0,
                AverageMessageProcessingTime = 0,
                Uptime = DateTimeOffset.UtcNow - _startTime,
                MemoryUsageBytes = GC.GetTotalMemory(false),
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["clusterConnected"] = true,
                    ["gatewayPort"] = GetConfigInt("gatewayPort", 30000)
                }
            };
            return Task.FromResult<IRuntimeStatistics>(stats);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _ = ShutdownAsync();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        public Task<string> RequestHumanInputAsync(string requestingAgentId, string prompt, string instructions, CancellationToken cancellationToken = default)
            => throw new NotImplementedException("Human input is not supported by OrleansAdapter.");

        private string GetConfigString(string key, string fallback)
            => _configuration.TryGetValue(key, out var v) && v != null ? v.ToString() ?? fallback : fallback;

        private int GetConfigInt(string key, int fallback)
            => _configuration.TryGetValue(key, out var v) && int.TryParse(v?.ToString(), out var n) ? n : fallback;

        private sealed class OrleansRuntimeStatistics : IRuntimeStatistics
        {
            public int ActiveActorCount { get; init; }
            public long TotalMessagesProcessed { get; init; }
            public double MessagesPerSecond { get; init; }
            public double AverageMessageProcessingTime { get; init; }
            public TimeSpan Uptime { get; init; }
            public long MemoryUsageBytes { get; init; }
            public IReadOnlyDictionary<string, object> AdditionalMetrics { get; init; } = new Dictionary<string, object>();
        }
    }
}
