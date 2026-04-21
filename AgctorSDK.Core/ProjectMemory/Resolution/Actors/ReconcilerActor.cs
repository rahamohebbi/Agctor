using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Observability;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Actors;

/// <summary>
/// One per project. Consumes <see cref="MentionObserved"/> and <see cref="SessionSummary"/>,
/// uses the mention index to find candidate entities, and dispatches <see cref="ResolveCandidate"/>
/// messages to the owning resolution actor via the runtime adapter.
/// </summary>
/// <remarks>
/// Resolution actor addressing is delegated to <see cref="IResolutionActorAddressing.ActorIdFor"/>
/// so tests and production can map (projectId, entityKey) to actor ids consistently.
/// Duplicate work is coalesced by <c>(mentionId, candidateKey)</c> within
/// <see cref="ResolutionPolicy.Reconciler"/>.<see cref="ReconcilerOptions.CoalesceWindowMs"/>.
/// </remarks>
public sealed class ReconcilerActor : IActor
{
    private readonly string _projectId;
    private readonly IActorRuntimeAdapter _runtime;
    private readonly MentionIndexActor _index;
    private readonly IResolutionActorAddressing _addressing;
    private ResolutionPolicy _policy;
    private readonly ResolutionMetrics? _metrics;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentlyDispatched = new(StringComparer.Ordinal);
    private ActorState _state = ActorState.Initializing;

    public string Id { get; }
    public string ActorType => nameof(ReconcilerActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public ReconcilerActor(
        string id,
        string projectId,
        IActorRuntimeAdapter runtime,
        MentionIndexActor index,
        IResolutionActorAddressing addressing,
        ResolutionPolicy? policy = null,
        ResolutionMetrics? metrics = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        _projectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _addressing = addressing ?? throw new ArgumentNullException(nameof(addressing));
        _policy = policy ?? ResolutionPolicy.CreateDefault();
        _metrics = metrics;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Stopped, "Shutdown");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        switch (envelope.Payload)
        {
            case MentionObserved mo:
                _metrics?.Increment(ResolutionMetrics.Keys.MentionsObserved(_projectId));
                await HandleMentionAsync(mo, Array.Empty<string>(), Array.Empty<string>(), cancellationToken);
                return new MessageEnvelope(new { Ok = true, Type = nameof(MentionObserved) });
            case SessionSummary ss:
                foreach (var m in ss.Mentions)
                {
                    _metrics?.Increment(ResolutionMetrics.Keys.MentionsObserved(_projectId));
                    await HandleMentionAsync(new MentionObserved { Mention = m }, ss.AssertedFacts, ss.NegativeAssertions, cancellationToken);
                }
                return new MessageEnvelope(new { Ok = true, Type = nameof(SessionSummary), Count = ss.Mentions.Count });
            case ReloadPolicy rp:
                _policy = rp.Policy ?? _policy;
                // Drop the coalesce memory so the new window takes immediate effect.
                _recentlyDispatched.Clear();
                return new MessageEnvelope(new { Ok = true, Type = nameof(ReloadPolicy) });
            default:
                return new MessageEnvelope(new { Error = "unsupported", Type = envelope.Payload?.GetType().Name });
        }
    }

    private async Task HandleMentionAsync(
        MentionObserved observed,
        IReadOnlyList<string> facts,
        IReadOnlyList<string> negatives,
        CancellationToken ct)
    {
        var surface = observed.Mention?.SurfaceForm;
        if (string.IsNullOrWhiteSpace(surface)) return;

        var lookup = _index.Lookup(surface);
        if (lookup.Candidates.Count == 0) return;

        foreach (var cand in lookup.Candidates)
        {
            // Coalesce duplicate work for the same (mention, candidate) inside the window.
            var coalesceKey = $"{observed.Mention!.MentionId}|{cand.EntityKey}";
            var windowMs = _policy.Reconciler?.CoalesceWindowMs ?? 2000;
            if (windowMs > 0 && _recentlyDispatched.TryGetValue(coalesceKey, out var last))
            {
                if ((DateTimeOffset.UtcNow - last).TotalMilliseconds < windowMs)
                {
                    _metrics?.Increment(ResolutionMetrics.Keys.CandidatesCoalesced(_projectId));
                    continue;
                }
            }
            _recentlyDispatched[coalesceKey] = DateTimeOffset.UtcNow;
            _metrics?.Increment(ResolutionMetrics.Keys.CandidatesDispatched(_projectId));

            var rc = new ResolveCandidate
            {
                Mention = observed.Mention!,
                CandidateEntityKey = cand.EntityKey,
                CandidateEntityPath = cand.EntityPath,
                TotalEntitiesMatchingSurface = lookup.Candidates.Count,
                SessionAssertedFacts = new List<string>(facts),
                SessionNegativeAssertions = new List<string>(negatives)
            };
            var actorId = _addressing.ActorIdFor(_projectId, cand.EntityKey);
            await _runtime.SendMessageAsync(actorId, rc, senderId: Id, cancellationToken: ct);
        }

        // Opportunistic cleanup: drop any coalesce entries older than 10x window to bound memory.
        TrimCoalesceMap();
    }

    private void TrimCoalesceMap()
    {
        var windowMs = _policy.Reconciler?.CoalesceWindowMs ?? 2000;
        var cutoff = DateTimeOffset.UtcNow.AddMilliseconds(-10L * windowMs);
        foreach (var kv in _recentlyDispatched)
        {
            if (kv.Value < cutoff)
                _recentlyDispatched.TryRemove(kv.Key, out _);
        }
    }

    private void ChangeState(ActorState newState, string? reason)
    {
        var prev = _state;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(prev, newState, reason));
    }
}

/// <summary>
/// Strategy for computing a resolution actor id from a project + entity key. Kept as an interface
/// so tests can use any convention and runtime adapters can translate to grains if needed.
/// </summary>
public interface IResolutionActorAddressing
{
    string ActorIdFor(string projectId, string entityKey);
    string ReconcilerIdFor(string projectId);
    string MentionIndexIdFor(string projectId);
    string SupervisorIdFor(string projectId);
}

/// <summary>Default convention: <c>res:&lt;project&gt;:&lt;entity&gt;</c>, <c>rec:&lt;project&gt;</c>, etc.</summary>
public sealed class DefaultResolutionAddressing : IResolutionActorAddressing
{
    public string ActorIdFor(string projectId, string entityKey) => $"res:{projectId}:{entityKey}";
    public string ReconcilerIdFor(string projectId) => $"rec:{projectId}";
    public string MentionIndexIdFor(string projectId) => $"midx:{projectId}";
    public string SupervisorIdFor(string projectId) => $"ressup:{projectId}";
}
