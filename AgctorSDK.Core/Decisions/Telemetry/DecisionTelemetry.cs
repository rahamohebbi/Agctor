using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgctorSDK.Core.Utils.Observability.Metrics;

namespace AgctorSDK.Core.Decisions
{
    public enum DecisionEventKind
    {
        DecisionRequested,
        ProviderSelected,
        DecisionCompleted,
        DecisionEscalated,
        DecisionFailed
    }

    /// <summary>
    /// One fabric event. Payloads stay small on purpose: provider, confidence, latency, cost. No reasoning trace.
    /// </summary>
    public sealed class DecisionEvent
    {
        public DecisionEventKind Kind { get; init; }
        public string? DecisionId { get; init; }
        public string? ActorId { get; init; }
        public DecisionType? Type { get; init; }
        public string? Provider { get; init; }
        public string? PreviousProvider { get; init; }
        public double? Confidence { get; init; }
        public double? LatencyMs { get; init; }
        public decimal? Cost { get; init; }
        public bool? Escalated { get; init; }
        public string? Detail { get; init; }
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    }

    public interface IDecisionEventListener
    {
        void OnEvent(DecisionEvent decisionEvent);
    }

    public interface IDecisionTelemetry
    {
        void Requested(DecisionRequest request, DecisionContext context);
        void ProviderSelected(DecisionRequest request, IDecisionProvider provider, DecisionContext context);
        void Completed(DecisionRequest request, DecisionResult result, DecisionContext context);
        void Escalated(DecisionRequest request, string fromProvider, string toProvider, double confidence, DecisionContext context);
        void Failed(DecisionRequest request, string? provider, Exception? error, DecisionContext context);
    }

    /// <summary>
    /// Used when the host did not register telemetry. Keeps the service constructor optional.
    /// </summary>
    public sealed class NullDecisionTelemetry : IDecisionTelemetry
    {
        public static readonly NullDecisionTelemetry Instance = new NullDecisionTelemetry();

        private NullDecisionTelemetry()
        {
        }

        public void Requested(DecisionRequest request, DecisionContext context)
        {
        }

        public void ProviderSelected(DecisionRequest request, IDecisionProvider provider, DecisionContext context)
        {
        }

        public void Completed(DecisionRequest request, DecisionResult result, DecisionContext context)
        {
        }

        public void Escalated(DecisionRequest request, string fromProvider, string toProvider, double confidence, DecisionContext context)
        {
        }

        public void Failed(DecisionRequest request, string? provider, Exception? error, DecisionContext context)
        {
        }
    }

    /// <summary>
    /// Emits the MVP decision events and the metric names the later analytics work will use.
    /// OpenTelemetry is already a dependency, so instruments are created on a <see cref="Meter"/> a host can export.
    /// </summary>
    public sealed class DecisionTelemetry : IDecisionTelemetry, IDisposable
    {
        public const string MeterName = "Agctor.DecisionFabric";

        private readonly Meter _meter;
        private readonly Histogram<double> _latency;
        private readonly Histogram<double> _cost;
        private readonly Histogram<double> _confidence;
        private readonly Counter<long> _escalations;
        private readonly Counter<long> _failures;
        private readonly IReadOnlyList<IDecisionEventListener> _listeners;
        private readonly IMetricsCollector? _metrics;

        public DecisionTelemetry(IEnumerable<IDecisionEventListener>? listeners = null, IMetricsCollector? metrics = null)
        {
            _listeners = listeners == null
                ? new List<IDecisionEventListener>()
                : new List<IDecisionEventListener>(listeners);
            _metrics = metrics;
            _meter = new Meter(MeterName);
            _latency = _meter.CreateHistogram<double>(MetricsConstants.Decisions.Latency, unit: "ms");
            _cost = _meter.CreateHistogram<double>(MetricsConstants.Decisions.Cost);
            _confidence = _meter.CreateHistogram<double>(MetricsConstants.Decisions.Confidence);
            _escalations = _meter.CreateCounter<long>(MetricsConstants.Decisions.Escalations);
            _failures = _meter.CreateCounter<long>(MetricsConstants.Decisions.ProviderFailures);
        }

        public void Requested(DecisionRequest request, DecisionContext context)
        {
            Publish(new DecisionEvent
            {
                Kind = DecisionEventKind.DecisionRequested,
                DecisionId = request.Id,
                ActorId = context?.ActorId,
                Type = request.Type
            });
        }

        public void ProviderSelected(DecisionRequest request, IDecisionProvider provider, DecisionContext context)
        {
            Publish(new DecisionEvent
            {
                Kind = DecisionEventKind.ProviderSelected,
                DecisionId = request.Id,
                ActorId = context?.ActorId,
                Type = request.Type,
                Provider = provider.Name
            });
        }

        public void Completed(DecisionRequest request, DecisionResult result, DecisionContext context)
        {
            var tags = MetricTags(result.Provider, request.Type, result.Escalated);
            var tagList = ToTagList(tags);
            _latency.Record(result.Latency.TotalMilliseconds, tagList);
            _cost.Record((double)(result.Cost ?? 0m), tagList);
            _confidence.Record(result.Confidence, tagList);
            _metrics?.RecordHistogram(MetricsConstants.Decisions.Latency, result.Latency.TotalMilliseconds, tags);
            _metrics?.RecordHistogram(MetricsConstants.Decisions.Cost, (double)(result.Cost ?? 0m), tags);
            _metrics?.RecordHistogram(MetricsConstants.Decisions.Confidence, result.Confidence, tags);

            Publish(new DecisionEvent
            {
                Kind = DecisionEventKind.DecisionCompleted,
                DecisionId = result.DecisionId ?? request.Id,
                ActorId = context?.ActorId,
                Type = request.Type,
                Provider = result.Provider,
                PreviousProvider = result.PreviousProvider,
                Confidence = result.Confidence,
                LatencyMs = result.Latency.TotalMilliseconds,
                Cost = result.Cost,
                Escalated = result.Escalated
            });
        }

        public void Escalated(DecisionRequest request, string fromProvider, string toProvider, double confidence, DecisionContext context)
        {
            var tags = MetricTags(toProvider, request.Type, escalated: true);
            _escalations.Add(1, ToTagList(tags));
            _metrics?.IncrementCounter(MetricsConstants.Decisions.Escalations, 1, tags);

            Publish(new DecisionEvent
            {
                Kind = DecisionEventKind.DecisionEscalated,
                DecisionId = request.Id,
                ActorId = context?.ActorId,
                Type = request.Type,
                Provider = toProvider,
                PreviousProvider = fromProvider,
                Confidence = confidence,
                Escalated = true
            });
        }

        public void Failed(DecisionRequest request, string? provider, Exception? error, DecisionContext context)
        {
            var tags = MetricTags(provider, request.Type, escalated: null);
            _failures.Add(1, ToTagList(tags));
            _metrics?.IncrementCounter(MetricsConstants.Decisions.ProviderFailures, 1, tags);

            Publish(new DecisionEvent
            {
                Kind = DecisionEventKind.DecisionFailed,
                DecisionId = request.Id,
                ActorId = context?.ActorId,
                Type = request.Type,
                Provider = provider,
                Detail = error?.Message
            });
        }

        public void Dispose()
        {
            _meter.Dispose();
        }

        private void Publish(DecisionEvent decisionEvent)
        {
            foreach (var listener in _listeners)
            {
                listener.OnEvent(decisionEvent);
            }
        }

        private static KeyValuePair<string, object>[] MetricTags(string? provider, DecisionType type, bool? escalated)
        {
            return new[]
            {
                new KeyValuePair<string, object>("provider", provider ?? "none"),
                new KeyValuePair<string, object>("decision_type", type.ToString()),
                new KeyValuePair<string, object>("escalated", escalated ?? false)
            };
        }

        private static TagList ToTagList(KeyValuePair<string, object>[] tags)
        {
            var list = new TagList();
            foreach (var tag in tags)
            {
                list.Add(tag.Key, tag.Value);
            }

            return list;
        }
    }
}
