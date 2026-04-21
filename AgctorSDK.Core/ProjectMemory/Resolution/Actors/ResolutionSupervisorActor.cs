using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Observability;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using AgctorSDK.Core.ProjectMemory.Resolution.Trace;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Actors;

/// <summary>
/// Spawns and owns the resolution subsystem for one project: the mention index, the reconciler,
/// and one <see cref="ResolutionActor"/> per entity. Holds no mutable state of its own beyond
/// lifecycle references so it can rehydrate children by re-running the same construction logic.
/// </summary>
/// <remarks>
/// This supervisor is wired to the <see cref="IActorRuntimeAdapter"/> so it is location-transparent
/// across in-memory, Orleans, and Proto.Actor backends (PRD-018 §9).
/// </remarks>
public sealed class ResolutionSupervisorActor : IActor
{
    private readonly IActorRuntimeAdapter _runtime;
    private readonly string _projectId;
    private readonly string _projectRoot;
    private ResolutionPolicy _policy;
    private readonly IReadOnlyList<ISignalProducer> _producers;
    private readonly IResolutionActorAddressing _addressing;
    private readonly IResolutionIntentSink _intentSink;
    private readonly IResolveSpanSink _spanSink;
    private readonly ResolutionMetrics? _metrics;
    private MentionIndexActor? _index;
    private ReconcilerActor? _reconciler;
    private readonly List<ResolutionActor> _entityActors = new();
    private ActorState _state = ActorState.Initializing;

    public string Id { get; }
    public string ActorType => nameof(ResolutionSupervisorActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public ResolutionSupervisorActor(
        string id,
        string projectId,
        string projectRoot,
        IActorRuntimeAdapter runtime,
        ResolutionPolicy policy,
        IReadOnlyList<ISignalProducer> producers,
        IResolutionActorAddressing addressing,
        IResolutionIntentSink? intentSink = null,
        ResolutionMetrics? metrics = null,
        IResolveSpanSink? spanSink = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        _projectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
        _projectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _producers = producers ?? throw new ArgumentNullException(nameof(producers));
        _addressing = addressing ?? throw new ArgumentNullException(nameof(addressing));
        _intentSink = intentSink ?? new NullResolutionIntentSink();
        _spanSink = spanSink ?? new NullResolveSpanSink();
        _metrics = metrics;
    }

    public MentionIndexActor? Index => _index;
    public ReconcilerActor? Reconciler => _reconciler;
    public IReadOnlyList<ResolutionActor> EntityActors => _entityActors;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // The supervisor just registers itself and becomes Active; real work happens in SpawnAllAsync.
        ChangeState(ActorState.Active, "Initialized");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Build the mention index from <paramref name="entities"/> and spawn per-entity resolution
    /// actors + the reconciler. Safe to call once; for rehydration, call <see cref="ShutdownAllAsync"/>
    /// first.
    /// </summary>
    public async Task SpawnAllAsync(IReadOnlyList<EntityRecord> entities, CancellationToken cancellationToken = default)
    {
        if (entities == null) throw new ArgumentNullException(nameof(entities));

        var indexed = entities.Select(e => new MentionIndexActor.IndexedEntity
        {
            EntityKey = e.EntityKey,
            EntityPath = e.RootPath,
            DisplayName = e.Metadata?.DisplayName ?? e.EntityKey,
            Aliases = e.Metadata?.Aliases ?? new List<string>()
        }).ToList();

        var indexId = _addressing.MentionIndexIdFor(_projectId);
        _index = await _runtime.SpawnActorAsync(indexId, (id) => new MentionIndexActor(id, indexed), cancellationToken: cancellationToken);

        foreach (var e in entities)
        {
            var actorId = _addressing.ActorIdFor(_projectId, e.EntityKey);
            var store = new ResolutionEdgeStore(e.RootPath);
            var display = e.Metadata?.DisplayName ?? e.EntityKey;
            var aliases = e.Metadata?.Aliases ?? new List<string>();
            var actor = await _runtime.SpawnActorAsync(actorId, (id) =>
                new ResolutionActor(id, e.EntityKey, display, aliases, store, _policy, _producers, _intentSink, _metrics, _projectId, _spanSink),
                cancellationToken: cancellationToken);
            _entityActors.Add(actor);
        }

        var recId = _addressing.ReconcilerIdFor(_projectId);
        _reconciler = await _runtime.SpawnActorAsync(recId, (id) => new ReconcilerActor(id, _projectId, _runtime, _index!, _addressing, _policy, _metrics), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Broadcast a <see cref="Messages.ReloadPolicy"/> to the reconciler and all entity actors so the
    /// subsystem picks up a new <c>.agctor/resolution.yaml</c> without a restart. Safe to call from a
    /// file watcher or admin endpoint.
    /// </summary>
    public async Task ReloadPolicyAsync(ResolutionPolicy newPolicy, string? changedBy = null, CancellationToken cancellationToken = default)
    {
        if (newPolicy == null) throw new ArgumentNullException(nameof(newPolicy));
        _policy = newPolicy;
        var msg = new Messages.ReloadPolicy { Policy = newPolicy, ChangedBy = changedBy };
        if (_reconciler != null)
            await _runtime.SendMessageAsync(_reconciler.Id, msg, senderId: Id, cancellationToken: cancellationToken);
        foreach (var a in _entityActors)
            await _runtime.SendMessageAsync(a.Id, msg, senderId: Id, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Replace the mention index projection (e.g. after a new entity folder is created). Runs
    /// against the existing <see cref="MentionIndexActor"/> instance without restarting it so no
    /// in-flight lookups are lost.
    /// </summary>
    public void RebuildIndex(IReadOnlyList<EntityRecord> entities)
    {
        if (_index == null) return;
        var indexed = (entities ?? Array.Empty<EntityRecord>()).Select(e => new MentionIndexActor.IndexedEntity
        {
            EntityKey = e.EntityKey,
            EntityPath = e.RootPath,
            DisplayName = e.Metadata?.DisplayName ?? e.EntityKey,
            Aliases = e.Metadata?.Aliases ?? new List<string>()
        });
        _index.Rebuild(indexed);
    }

    public async Task ShutdownAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var a in _entityActors)
            await _runtime.StopActorAsync(a.Id, cancellationToken);
        _entityActors.Clear();

        if (_reconciler != null)
        {
            await _runtime.StopActorAsync(_reconciler.Id, cancellationToken);
            _reconciler = null;
        }

        if (_index != null)
        {
            await _runtime.StopActorAsync(_index.Id, cancellationToken);
            _index = null;
        }
    }

    public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        // Supervisor currently has no request-response protocol; children are reached directly by id.
        return Task.FromResult<IMessageEnvelope>(new MessageEnvelope(new { Ok = true }));
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await ShutdownAllAsync(cancellationToken);
        ChangeState(ActorState.Stopped, "Shutdown");
    }

    private void ChangeState(ActorState newState, string? reason)
    {
        var prev = _state;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(prev, newState, reason));
    }
}
