using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Observability;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using AgctorSDK.Core.ProjectMemory.Resolution.Trace;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Actors;

/// <summary>
/// One per canonical entity (<c>res:&lt;projectId&gt;:&lt;entityKey&gt;</c>). Owns the on-disk evidence
/// log for that entity and is the only writer of its <c>.resolution/</c> sidecar.
/// Message-routed to keep all edge mutations serialized through the mailbox.
/// </summary>
public sealed class ResolutionActor : IActor
{
    private readonly string _entityKey;
    private readonly string _displayName;
    private readonly IReadOnlyList<string> _aliases;
    private readonly ResolutionEdgeStore _store;
    private ResolutionPolicy _policy;
    private readonly IReadOnlyList<ISignalProducer> _producers;
    private readonly IResolutionIntentSink _intentSink;
    private readonly IResolveSpanSink _spanSink;
    private readonly ResolutionMetrics? _metrics;
    private readonly string _projectId;
    private ActorState _state = ActorState.Initializing;

    public string Id { get; }
    public string ActorType => nameof(ResolutionActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public ResolutionActor(
        string id,
        string entityKey,
        string displayName,
        IReadOnlyList<string> aliases,
        ResolutionEdgeStore store,
        ResolutionPolicy policy,
        IReadOnlyList<ISignalProducer> producers,
        IResolutionIntentSink? intentSink = null,
        ResolutionMetrics? metrics = null,
        string projectId = "default",
        IResolveSpanSink? spanSink = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        _entityKey = entityKey ?? throw new ArgumentNullException(nameof(entityKey));
        _displayName = string.IsNullOrWhiteSpace(displayName) ? entityKey : displayName;
        _aliases = aliases ?? System.Array.Empty<string>();
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _producers = producers ?? throw new ArgumentNullException(nameof(producers));
        _intentSink = intentSink ?? new NullResolutionIntentSink();
        _spanSink = spanSink ?? new NullResolveSpanSink();
        _metrics = metrics;
        _projectId = projectId ?? "default";
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialization completed");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Stopped, "Shutdown");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        return envelope.Payload switch
        {
            ResolveCandidate rc => await HandleResolveCandidateAsync(rc, cancellationToken),
            PromotionRequested p => await HandlePromotionAsync(p, cancellationToken),
            DemotionRequested d => await HandleDemotionAsync(d, cancellationToken),
            ReloadPolicy rp => HandleReloadPolicy(rp),
            _ => new MessageEnvelope(new { Error = "unsupported", Type = envelope.Payload?.GetType().Name })
        };
    }

    /// <summary>
    /// Run configured signal producers for the candidate, upsert the edge, and publish an
    /// EvidenceAppended describing the latest signal. Auto-promotion triggers here too when the
    /// thresholds + independence rule are satisfied.
    /// </summary>
    private async Task<IMessageEnvelope> HandleResolveCandidateAsync(ResolveCandidate rc, CancellationToken ct)
    {
        var ctx = new SignalContext
        {
            Mention = rc.Mention,
            CandidateEntityKey = rc.CandidateEntityKey,
            CandidateEntityPath = rc.CandidateEntityPath,
            CandidateDisplayName = _displayName,
            CandidateAliases = _aliases,
            TotalEntitiesMatchingSurface = rc.TotalEntitiesMatchingSurface <= 0 ? 1 : rc.TotalEntitiesMatchingSurface,
            SessionAssertedFacts = rc.SessionAssertedFacts ?? new List<string>(),
            SessionNegativeAssertions = rc.SessionNegativeAssertions ?? new List<string>()
        };

        var edgeId = ResolutionEdge.MakeEdgeId(rc.Mention, rc.CandidateEntityKey);
        var doc = _store.Load();
        var existing = doc.Edges.Find(e => e.EdgeId == edgeId);
        var edge = existing ?? new ResolutionEdge
        {
            EdgeId = edgeId,
            TargetEntityKey = rc.CandidateEntityKey,
            Mention = rc.Mention,
            State = ResolutionEdgeState.Soft
        };

        ResolutionSignal? lastAdded = null;
        foreach (var p in _producers)
        {
            var sig = p.Score(ctx, _policy);
            if (sig == null) continue;
            if (ContainsFingerprint(edge.Signals, sig)) continue;
            edge.Signals.Add(sig);
            lastAdded = sig;
        }

        edge.Confidence = ConfidenceCalculator.Compute(edge.Signals, _policy);
        edge.LastUpdatedAt = DateTimeOffset.UtcNow;

        if (edge.State == ResolutionEdgeState.Soft && edge.Confidence >= _policy.SoftThreshold)
        {
            // Already soft - just refresh. Below threshold we still persist so signals are not lost.
        }

        var autoPromoted = TryAutoPromote(edge);
        _store.Upsert(edge);
        if (autoPromoted != null)
        {
            _store.AppendPromotion(edge.EdgeId, autoPromoted);
            _metrics?.Increment(ResolutionMetrics.Keys.AutoPromotions(_projectId));
        }
        _metrics?.Increment(existing == null
            ? ResolutionMetrics.Keys.EdgesCreated(_projectId)
            : ResolutionMetrics.Keys.EdgesUpdated(_projectId));

        // Emit an intent draft so sinks (sidecar or real ingest) can record the proposal.
        if (edge.State == ResolutionEdgeState.Soft && edge.Confidence >= _policy.SoftThreshold)
        {
            await _intentSink.ApplyAsync(new IngestIntentDraft
            {
                EdgeId = edge.EdgeId,
                Kind = IntentKind.SoftLink,
                Mention = edge.Mention,
                TargetEntityKey = edge.TargetEntityKey,
                TargetEntityPath = rc.CandidateEntityPath,
                Confidence = edge.Confidence,
                Reason = "confidence crossed soft threshold"
            }, ct);
            _metrics?.Increment(ResolutionMetrics.Keys.IntentsEmitted(_projectId));
        }
        else if (autoPromoted != null && edge.State == ResolutionEdgeState.Hard)
        {
            await _intentSink.ApplyAsync(new IngestIntentDraft
            {
                EdgeId = edge.EdgeId,
                Kind = IntentKind.HardLink,
                Mention = edge.Mention,
                TargetEntityKey = edge.TargetEntityKey,
                TargetEntityPath = rc.CandidateEntityPath,
                Confidence = edge.Confidence,
                Reason = autoPromoted.Reason
            }, ct);
            _metrics?.Increment(ResolutionMetrics.Keys.IntentsEmitted(_projectId));
        }

        // Emit a trace span so playground / review UIs can show Input · Evidence · Outcome for
        // this candidate (PRD-018 §5.7 U1). Null-sink implementations skip the allocation.
        try
        {
            var spanDetail = ResolveSpanDetail.Build(
                rc,
                edge,
                new List<LookupCandidate>
                {
                    new() { EntityKey = rc.CandidateEntityKey, EntityPath = rc.CandidateEntityPath, SurfaceScore = 1.0, UniquenessScore = 1.0 / (rc.TotalEntitiesMatchingSurface <= 0 ? 1 : rc.TotalEntitiesMatchingSurface) }
                });
            await _spanSink.EmitAsync(spanDetail, ct).ConfigureAwait(false);
        }
        catch
        {
            // Tracing is always best-effort — never fail a resolve because of a downstream sink.
        }

        var evidence = new EvidenceAppended
        {
            EdgeId = edge.EdgeId,
            TargetEntityKey = edge.TargetEntityKey,
            State = edge.State,
            Confidence = edge.Confidence,
            AddedSignal = lastAdded ?? new ResolutionSignal()
        };
        return new MessageEnvelope(evidence);
    }

    private async Task<IMessageEnvelope> HandlePromotionAsync(PromotionRequested msg, CancellationToken ct)
    {
        var doc = _store.Load();
        var edge = doc.Edges.Find(e => e.EdgeId == msg.EdgeId);
        if (edge == null)
            return new MessageEnvelope(new { Error = "edge-not-found", msg.EdgeId });

        if (edge.State == ResolutionEdgeState.Hard)
            return new MessageEnvelope(new LinkStateChanged { EdgeId = edge.EdgeId, From = edge.State, To = edge.State, ConfidenceSnapshot = edge.Confidence, By = msg.RequestedBy });

        var prom = new ResolutionPromotion
        {
            From = edge.State,
            To = ResolutionEdgeState.Hard,
            By = msg.RequestedBy,
            Reason = msg.Reason,
            ConfidenceSnapshot = edge.Confidence,
            ThresholdUsed = _policy.HardThreshold,
            SignalsSnapshot = Snapshot(edge.Signals)
        };
        edge.Promotions.Add(prom);
        edge.State = ResolutionEdgeState.Hard;
        edge.LastUpdatedAt = DateTimeOffset.UtcNow;
        _store.Upsert(edge);
        _store.AppendPromotion(edge.EdgeId, prom);

        await _intentSink.ApplyAsync(new IngestIntentDraft
        {
            EdgeId = edge.EdgeId,
            Kind = IntentKind.HardLink,
            Mention = edge.Mention,
            TargetEntityKey = edge.TargetEntityKey,
            Confidence = edge.Confidence,
            Reason = msg.Reason
        }, ct);
        _metrics?.Increment(ResolutionMetrics.Keys.OperatorPromotions(_projectId));
        _metrics?.Increment(ResolutionMetrics.Keys.IntentsEmitted(_projectId));

        return new MessageEnvelope(new LinkStateChanged
        {
            EdgeId = edge.EdgeId,
            TargetEntityKey = edge.TargetEntityKey,
            From = prom.From,
            To = prom.To,
            ConfidenceSnapshot = prom.ConfidenceSnapshot,
            By = prom.By
        });
    }

    private async Task<IMessageEnvelope> HandleDemotionAsync(DemotionRequested msg, CancellationToken ct)
    {
        var doc = _store.Load();
        var edge = doc.Edges.Find(e => e.EdgeId == msg.EdgeId);
        if (edge == null)
            return new MessageEnvelope(new { Error = "edge-not-found", msg.EdgeId });

        var to = msg.Reject ? ResolutionEdgeState.Rejected : ResolutionEdgeState.Soft;
        var prom = new ResolutionPromotion
        {
            From = edge.State,
            To = to,
            By = msg.RequestedBy,
            Reason = msg.Reason,
            ConfidenceSnapshot = edge.Confidence,
            ThresholdUsed = _policy.HardThreshold,
            SignalsSnapshot = Snapshot(edge.Signals)
        };
        edge.Promotions.Add(prom);
        edge.State = to;
        edge.LastUpdatedAt = DateTimeOffset.UtcNow;
        _store.Upsert(edge);
        _store.AppendPromotion(edge.EdgeId, prom);

        await _intentSink.ApplyAsync(new IngestIntentDraft
        {
            EdgeId = edge.EdgeId,
            Kind = msg.Reject ? IntentKind.Reject : IntentKind.Demote,
            Mention = edge.Mention,
            TargetEntityKey = edge.TargetEntityKey,
            Confidence = edge.Confidence,
            Reason = msg.Reason
        }, ct);
        _metrics?.Increment(msg.Reject
            ? ResolutionMetrics.Keys.Rejections(_projectId)
            : ResolutionMetrics.Keys.Demotions(_projectId));
        _metrics?.Increment(ResolutionMetrics.Keys.IntentsEmitted(_projectId));

        return new MessageEnvelope(new LinkStateChanged
        {
            EdgeId = edge.EdgeId,
            TargetEntityKey = edge.TargetEntityKey,
            From = prom.From,
            To = prom.To,
            ConfidenceSnapshot = prom.ConfidenceSnapshot,
            By = prom.By
        });
    }

    /// <summary>
    /// Auto-promotion gate: confidence past hardThreshold, no negatives, and at least
    /// <c>MinIndependentSignalFamiliesForAutoPromote</c> distinct positive signal kinds.
    /// Returns the promotion row written, or null if no promotion occurred.
    /// </summary>
    private ResolutionPromotion? TryAutoPromote(ResolutionEdge edge)
    {
        if (!_policy.Review.AutoPromote) return null;
        if (edge.State != ResolutionEdgeState.Soft) return null;
        if (edge.Confidence < _policy.HardThreshold) return null;
        if (ConfidenceCalculator.IndependentPositiveFamilies(edge.Signals) < _policy.Review.MinIndependentSignalFamiliesForAutoPromote) return null;
        foreach (var s in edge.Signals) if (s.IsNegative) return null;

        var prom = new ResolutionPromotion
        {
            From = edge.State,
            To = ResolutionEdgeState.Hard,
            By = "auto",
            Reason = $"confidence {edge.Confidence:F2} >= hardThreshold {_policy.HardThreshold:F2}",
            ConfidenceSnapshot = edge.Confidence,
            ThresholdUsed = _policy.HardThreshold,
            SignalsSnapshot = Snapshot(edge.Signals)
        };
        edge.Promotions.Add(prom);
        edge.State = ResolutionEdgeState.Hard;
        edge.LastUpdatedAt = DateTimeOffset.UtcNow;
        return prom;
    }

    private IMessageEnvelope HandleReloadPolicy(ReloadPolicy msg)
    {
        // Swap the policy reference atomically; existing edges keep their persisted signals but future
        // confidence re-computations and auto-promotions use the new thresholds / weights.
        _policy = msg.Policy ?? _policy;
        return new MessageEnvelope(new { Reloaded = true, Entity = _entityKey });
    }

    private static bool ContainsFingerprint(List<ResolutionSignal> list, ResolutionSignal sig)
    {
        foreach (var s in list)
            if (s.Kind == sig.Kind && s.InputsFingerprint == sig.InputsFingerprint)
                return true;
        return false;
    }

    private static List<ResolutionSignal> Snapshot(List<ResolutionSignal> src)
    {
        var copy = new List<ResolutionSignal>(src.Count);
        foreach (var s in src)
        {
            copy.Add(new ResolutionSignal
            {
                Kind = s.Kind,
                Score = s.Score,
                Weight = s.Weight,
                Rationale = s.Rationale,
                ProducedBy = s.ProducedBy,
                InputsFingerprint = s.InputsFingerprint,
                ObservedAt = s.ObservedAt,
                IsNegative = s.IsNegative
            });
        }
        return copy;
    }

    private void ChangeState(ActorState newState, string? reason)
    {
        var prev = _state;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(prev, newState, reason));
    }
}
