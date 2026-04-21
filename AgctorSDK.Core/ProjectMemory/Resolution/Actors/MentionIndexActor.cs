using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Actors;

/// <summary>
/// Surface -&gt; candidates lookup for one project. Pure projection over entity metadata; rebuilt
/// from scratch when the registry changes. No authority on its own; the reconciler uses it as a
/// cheap first pass before dispatching ResolveCandidate to the owning resolution actor.
/// </summary>
public sealed class MentionIndexActor : IActor
{
    /// <summary>Compact record the index needs per entity; supplied at construction.</summary>
    public sealed class IndexedEntity
    {
        public string EntityKey { get; set; } = "";
        public string EntityPath { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public IReadOnlyList<string> Aliases { get; set; } = Array.Empty<string>();
    }

    private readonly Dictionary<string, List<IndexedEntity>> _bySurface = new(StringComparer.Ordinal);
    private ActorState _state = ActorState.Initializing;

    public string Id { get; }
    public string ActorType => nameof(MentionIndexActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public MentionIndexActor(string id, IEnumerable<IndexedEntity> entities)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        if (entities == null) throw new ArgumentNullException(nameof(entities));
        Rebuild(entities);
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

    public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IMessageEnvelope>(envelope.Payload switch
        {
            LookupBySurface lbs => new MessageEnvelope(Lookup(lbs.Text)),
            _ => new MessageEnvelope(new { Error = "unsupported", Type = envelope.Payload?.GetType().Name })
        });
    }

    /// <summary>
    /// Direct call variant for tests / in-process use. Returns candidates ordered by alias match
    /// strength, with uniqueness = 1/N baked in.
    /// </summary>
    public LookupResponse Lookup(string surface)
    {
        var response = new LookupResponse();
        var normalized = SurfaceNormalizer.Normalize(surface);
        if (string.IsNullOrEmpty(normalized)) return response;

        if (!_bySurface.TryGetValue(normalized, out var bucket) || bucket.Count == 0)
            return response;

        double unique = 1.0 / Math.Max(1, bucket.Count);
        foreach (var e in bucket)
        {
            response.Candidates.Add(new LookupCandidate
            {
                EntityKey = e.EntityKey,
                EntityPath = e.EntityPath,
                SurfaceScore = 1.0,
                UniquenessScore = unique
            });
        }
        return response;
    }

    /// <summary>Replace the projection wholesale (used after registry refresh).</summary>
    public void Rebuild(IEnumerable<IndexedEntity> entities)
    {
        _bySurface.Clear();
        foreach (var e in entities)
        {
            var seenForEntity = new HashSet<string>(StringComparer.Ordinal);
            foreach (var surface in EnumerateSurfaces(e))
            {
                var key = SurfaceNormalizer.Normalize(surface);
                if (string.IsNullOrEmpty(key)) continue;
                if (!seenForEntity.Add(key)) continue; // same entity, same normalized surface
                if (!_bySurface.TryGetValue(key, out var list))
                {
                    list = new List<IndexedEntity>();
                    _bySurface[key] = list;
                }
                list.Add(e);
            }
        }
    }

    private static IEnumerable<string> EnumerateSurfaces(IndexedEntity e)
    {
        if (!string.IsNullOrWhiteSpace(e.DisplayName)) yield return e.DisplayName;
        if (!string.IsNullOrWhiteSpace(e.EntityKey)) yield return e.EntityKey;
        if (e.Aliases != null)
            foreach (var a in e.Aliases)
                if (!string.IsNullOrWhiteSpace(a)) yield return a;
    }

    private void ChangeState(ActorState newState, string? reason)
    {
        var prev = _state;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(prev, newState, reason));
    }
}
