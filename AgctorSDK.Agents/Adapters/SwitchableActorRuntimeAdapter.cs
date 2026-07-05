using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Adapters;

/// <summary>
/// Delegates to the active runtime so the Host can swap InMemory / Orleans / Proto without restart.
/// </summary>
public sealed class SwitchableActorRuntimeAdapter : IActorRuntimeAdapter
{
    private readonly object _gate = new();
    private IActorRuntimeAdapter _inner;

    public SwitchableActorRuntimeAdapter(IActorRuntimeAdapter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>Atomically replace the active adapter (caller must init/shutdown around this).</summary>
    public void SetInner(IActorRuntimeAdapter inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        lock (_gate)
        {
            _inner = inner;
        }
    }

    public IActorRuntimeAdapter Current
    {
        get
        {
            lock (_gate)
            {
                return _inner;
            }
        }
    }

    public string Name => Current.Name;
    public string Version => Current.Version;
    public bool IsInitialized => Current.IsInitialized;
    public IReadOnlyDictionary<string, object> Configuration => Current.Configuration;

    public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned;
    public event EventHandler<ActorStoppedEventArgs>? ActorStopped;
    public event EventHandler<MessageSentEventArgs>? MessageSent;
    public event EventHandler<DeadLetterEventArgs>? DeadLetter;

    public Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default)
        => Current.InitializeAsync(configuration, cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
        => Current.ShutdownAsync(cancellationToken);

    public Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
        => Current.SpawnActorAsync<T>(actorId, initializationData, cancellationToken);

    public Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> actorFactory, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
        => Current.SpawnActorAsync(actorId, actorFactory, initializationData, cancellationToken);

    public Task RegisterActorAsync(IActor actor, CancellationToken cancellationToken = default)
        => Current.RegisterActorAsync(actor, cancellationToken);

    public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor
        => Current.GetActorAsync<T>(actorId, cancellationToken);

    public Task SendMessageAsync(string targetActorId, object message, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        => Current.SendMessageAsync(targetActorId, message, senderId, headers, cancellationToken);

    public Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) where TResponse : class
        => Current.SendMessageAsync<TResponse>(targetActorId, message, timeout, senderId, headers, cancellationToken);

    public Task StopActorAsync(string actorId, CancellationToken cancellationToken = default)
        => Current.StopActorAsync(actorId, cancellationToken);

    public Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default)
        => Current.GetActiveActorIdsAsync(cancellationToken);

    public Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        => Current.GetStatisticsAsync(cancellationToken);

    public Task<string> RequestHumanInputAsync(string requestingAgentId, string prompt, string instructions, CancellationToken cancellationToken = default)
        => Current.RequestHumanInputAsync(requestingAgentId, prompt, instructions, cancellationToken);

    public void Dispose() => Current.Dispose();
}
