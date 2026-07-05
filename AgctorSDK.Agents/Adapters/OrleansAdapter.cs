using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Orleans.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;

namespace AgctorSDK.Core.Adapters;

/// <summary>
/// Orleans client adapter: connects to the silo cluster, then runs agents via an in-process mailbox
/// (same semantics as InMemory) until distributed grains are wired for every agent type.
/// </summary>
public class OrleansAdapter : IActorRuntimeAdapter
{
    // Local mailbox engine — keeps AgentFactory / spawn / messaging working today.
    private readonly InMemoryActorRuntime _local = new();
    private bool _isDisposed;
    private bool _isInitialized;
    private readonly Dictionary<string, object> _configuration = new();
    private IHost? _clientHost;
    private IClusterClient? _clusterClient;
    private DateTimeOffset _startTime;
    private readonly ConcurrentDictionary<string, string> _knownActorIds = new();

#pragma warning disable CS0067
    public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned;
    public event EventHandler<ActorStoppedEventArgs>? ActorStopped;
    public event EventHandler<MessageSentEventArgs>? MessageSent;
    public event EventHandler<DeadLetterEventArgs>? DeadLetter;
#pragma warning restore CS0067

    public string Name => "Orleans";
    public string Version => "1.0.0-client";
    public bool IsInitialized => _isInitialized;
    public IReadOnlyDictionary<string, object> Configuration => _configuration;

    /// <summary>Connect Orleans client, verify silo health, then start the local actor engine.</summary>
    public async Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;
        foreach (var kvp in configuration) _configuration[kvp.Key] = kvp.Value;

        var clusterId = GetConfigString("clusterId", "agctor-dev");
        var serviceId = GetConfigString("serviceId", "agctor-host");
        var gatewayHost = GetConfigString("gatewayHost", "127.0.0.1");
        var gatewayPort = GetConfigInt("gatewayPort", 30000);

        _clientHost = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddOrleansClient(clientBuilder =>
                {
                    clientBuilder.Configure<ClusterOptions>(options =>
                    {
                        options.ClusterId = clusterId;
                        options.ServiceId = serviceId;
                    });

                    // Local Docker silo uses localhost clustering; remote gateways use static endpoints.
                    if (IsLocalGatewayHost(gatewayHost))
                        clientBuilder.UseLocalhostClustering(gatewayPort: gatewayPort);
                    else
                        clientBuilder.UseStaticClustering(new IPEndPoint(IPAddress.Parse(gatewayHost), gatewayPort));
                });
            })
            .Build();

        await _clientHost.StartAsync(cancellationToken).ConfigureAwait(false);
        _clusterClient = _clientHost.Services.GetRequiredService<IClusterClient>();

        var ping = await _clusterClient.GetGrain<IAgctorHealthGrain>(0).PingAsync().ConfigureAwait(false);
        if (!string.Equals(ping, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Orleans health grain returned unexpected value: {ping}");

        await _local.InitializeAsync(configuration, cancellationToken).ConfigureAwait(false);
        _startTime = DateTimeOffset.UtcNow;
        _isInitialized = true;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized) return;
        _knownActorIds.Clear();
        await _local.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        if (_clientHost != null)
        {
            await _clientHost.StopAsync(cancellationToken).ConfigureAwait(false);
            _clientHost.Dispose();
            _clientHost = null;
        }

        _clusterClient = null;
        _isInitialized = false;
    }

    public async Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
    {
        var actor = await _local.SpawnActorAsync<T>(actorId, initializationData, cancellationToken).ConfigureAwait(false);
        _knownActorIds[actorId] = typeof(T).Name;
        return actor;
    }

    public async Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> actorFactory, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
    {
        var actor = await _local.SpawnActorAsync(actorId, actorFactory, initializationData, cancellationToken).ConfigureAwait(false);
        _knownActorIds[actorId] = typeof(T).Name;
        return actor;
    }

    public Task RegisterActorAsync(IActor actor, CancellationToken cancellationToken = default)
    {
        _knownActorIds[actor.Id] = actor.ActorType;
        return _local.RegisterActorAsync(actor, cancellationToken);
    }

    public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor
        => _local.GetActorAsync<T>(actorId, cancellationToken);

    public Task SendMessageAsync(string targetActorId, object message, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        => _local.SendMessageAsync(targetActorId, message, senderId, headers, cancellationToken);

    public Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) where TResponse : class
        => _local.SendMessageAsync<TResponse>(targetActorId, message, timeout, senderId, headers, cancellationToken);

    public async Task StopActorAsync(string actorId, CancellationToken cancellationToken = default)
    {
        await _local.StopActorAsync(actorId, cancellationToken).ConfigureAwait(false);
        _knownActorIds.TryRemove(actorId, out _);
    }

    public Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default)
        => _local.GetActiveActorIdsAsync(cancellationToken);

    public async Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized || _clusterClient == null)
            throw new InvalidOperationException("Orleans runtime is not initialized.");

        var local = await _local.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        var merged = new Dictionary<string, object>(local.AdditionalMetrics)
        {
            ["clusterConnected"] = true,
            ["gatewayPort"] = GetConfigInt("gatewayPort", 30000),
            ["orleansTrackedActors"] = _knownActorIds.Count
        };

        return new OrleansRuntimeStatistics
        {
            ActiveActorCount = local.ActiveActorCount,
            TotalMessagesProcessed = local.TotalMessagesProcessed,
            MessagesPerSecond = local.MessagesPerSecond,
            AverageMessageProcessingTime = local.AverageMessageProcessingTime,
            Uptime = DateTimeOffset.UtcNow - _startTime,
            MemoryUsageBytes = local.MemoryUsageBytes,
            AdditionalMetrics = merged
        };
    }

    public Task<string> RequestHumanInputAsync(string requestingAgentId, string prompt, string instructions, CancellationToken cancellationToken = default)
        => _local.RequestHumanInputAsync(requestingAgentId, prompt, instructions, cancellationToken);

    public void Dispose()
    {
        if (_isDisposed) return;
        _ = ShutdownAsync();
        _local.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private static bool IsLocalGatewayHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

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
