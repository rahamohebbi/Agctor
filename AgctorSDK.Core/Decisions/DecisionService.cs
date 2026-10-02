using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Owns the decision lifecycle: select a provider, normalize the result, escalate on low confidence,
    /// and fall back when a provider fails. Latency on the result is the full wall time, including escalation.
    /// </summary>
    public sealed class DecisionService : IDecisionService
    {
        private const int MaxSummaryLength = 280;

        private readonly IDecisionRouter _router;
        private readonly DecisionFabricOptions _options;
        private readonly IDecisionTelemetry _telemetry;
        private readonly IReadOnlyList<IDecisionObserver> _observers;

        public DecisionService(
            IDecisionRouter router,
            DecisionFabricOptions? options = null,
            IDecisionTelemetry? telemetry = null,
            IEnumerable<IDecisionObserver>? observers = null)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _options = options ?? new DecisionFabricOptions();
            _telemetry = telemetry ?? NullDecisionTelemetry.Instance;
            _observers = (observers ?? Array.Empty<IDecisionObserver>()).Where(observer => observer != null).ToList();
        }

        public Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            return DecideAsync(request, new DecisionContext { Policy = request?.Policy }, cancellationToken);
        }

        public async Task<DecisionResult> DecideAsync(
            DecisionRequest request,
            DecisionContext context,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Id))
            {
                request.Id = Guid.NewGuid().ToString("N");
            }

            context ??= new DecisionContext();
            context.Policy ??= request.Policy;

            var started = Stopwatch.StartNew();
            _telemetry.Requested(request, context);

            var excluded = new List<string>(context.ExcludedProviders ?? new List<string>());
            Exception? lastError = null;
            var sawProviderFailure = false;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IDecisionProvider provider;
                try
                {
                    provider = await _router.SelectProviderAsync(request, Copy(context, excluded)).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    break;
                }

                if (excluded.Any(name => string.Equals(name, provider.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }

                _telemetry.ProviderSelected(request, provider, context);

                try
                {
                    var raw = await provider.DecideAsync(request, cancellationToken).ConfigureAwait(false);
                    var result = Normalize(request, raw, provider.Name);
                    result = await EscalateIfNeeded(request, context, provider, result, cancellationToken).ConfigureAwait(false);
                    started.Stop();
                    result.Latency = started.Elapsed;
                    _telemetry.Completed(request, result, context);
                    await NotifyAsync(request, result, cancellationToken).ConfigureAwait(false);
                    return result;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    sawProviderFailure = true;
                    excluded.Add(provider.Name);
                    _telemetry.Failed(request, provider.Name, ex, context);
                    if (!_options.Routing.EnableFallback)
                    {
                        break;
                    }
                }
            }

            if (!sawProviderFailure)
            {
                _telemetry.Failed(request, null, lastError, context);
            }

            throw new DecisionException(
                "Decision failed because no provider could answer.",
                request.Id,
                null,
                lastError);
        }

        private async Task<DecisionResult> EscalateIfNeeded(
            DecisionRequest request,
            DecisionContext context,
            IDecisionProvider provider,
            DecisionResult result,
            CancellationToken cancellationToken)
        {
            if (!_options.Routing.EnableEscalation)
            {
                return result;
            }

            // Rules are exact. The LLM is already the escalation target.
            if (provider.Profile.Role == DecisionEngineRole.Rules || provider.Profile.Role == DecisionEngineRole.Llm)
            {
                return result;
            }

            var threshold = Threshold(request);
            if (result.Confidence >= threshold)
            {
                return result;
            }

            var excluded = new List<string>(context.ExcludedProviders ?? new List<string>()) { provider.Name };
            IDecisionProvider next;
            try
            {
                next = await _router.SelectProviderAsync(request, new DecisionContext
                {
                    ActorId = context.ActorId,
                    ActorType = context.ActorType,
                    Policy = context.Policy ?? request.Policy,
                    ExcludedProviders = excluded,
                    Escalate = true
                }).ConfigureAwait(false);
            }
            catch (DecisionException)
            {
                // No stronger engine is allowed. Keep the low-confidence answer.
                return result;
            }

            _telemetry.Escalated(request, provider.Name, next.Name, result.Confidence, context);
            _telemetry.ProviderSelected(request, next, context);

            try
            {
                var escalatedRaw = await next.DecideAsync(request, cancellationToken).ConfigureAwait(false);
                var escalated = Normalize(request, escalatedRaw, next.Name);
                escalated.Escalated = true;
                escalated.PreviousProvider = provider.Name;
                escalated.Cost = Sum(result.Cost, escalated.Cost);
                return escalated;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The first answer is still usable. Record the failed escalation and return it.
                _telemetry.Failed(request, next.Name, ex, context);
                return result;
            }
        }

        private double Threshold(DecisionRequest request)
        {
            var value = request.Policy?.MinimumConfidence ?? _options.Routing.ConfidenceThreshold;
            if (value <= 0d || value > 1d)
            {
                return 0.70d;
            }

            return value;
        }

        private static DecisionResult Normalize(DecisionRequest request, DecisionResult? raw, string providerName)
        {
            var result = raw ?? new DecisionResult();
            result.DecisionId = string.IsNullOrWhiteSpace(request.Id) ? result.DecisionId : request.Id;
            result.Provider = string.IsNullOrWhiteSpace(result.Provider) ? providerName : result.Provider;
            result.Confidence = Math.Clamp(result.Confidence, 0d, 1d);

            if (!string.IsNullOrEmpty(result.ReasoningSummary) && result.ReasoningSummary.Length > MaxSummaryLength)
            {
                // Summaries are a label for operators, not a place to store hidden reasoning.
                result.ReasoningSummary = result.ReasoningSummary.Substring(0, MaxSummaryLength);
            }

            switch (request.Type)
            {
                case DecisionType.Choice:
                    result.Value = result.Value?.ToString() ?? string.Empty;
                    break;
                case DecisionType.Score:
                    var score = ToDouble(result.Value);
                    result.Value = ClampScore(request, score);
                    break;
                case DecisionType.Binary:
                    result.Value = ToBool(result.Value);
                    break;
            }

            return result;
        }

        private static double ClampScore(DecisionRequest request, double score)
        {
            var min = request.Constraints?.Min ?? 0d;
            var max = request.Constraints?.Max ?? 100d;
            if (max < min)
            {
                (min, max) = (max, min);
            }

            return Math.Clamp(score, min, max);
        }

        private static double ToDouble(object? value)
        {
            if (Providers.RuleDecisionProvider.TryDouble(value, out var number))
            {
                return number;
            }

            return 0d;
        }

        private static bool ToBool(object? value)
        {
            return value switch
            {
                bool flag => flag,
                string text when bool.TryParse(text, out var flag) => flag,
                string text => string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "1", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private static decimal? Sum(decimal? left, decimal? right)
        {
            if (!left.HasValue && !right.HasValue)
            {
                return null;
            }

            return (left ?? 0m) + (right ?? 0m);
        }

        private static DecisionContext Copy(DecisionContext context, List<string> excluded)
        {
            return new DecisionContext
            {
                ActorId = context.ActorId,
                ActorType = context.ActorType,
                Policy = context.Policy,
                ExcludedProviders = excluded.ToList(),
                Escalate = context.Escalate
            };
        }

        private async Task NotifyAsync(DecisionRequest request, DecisionResult result, CancellationToken cancellationToken)
        {
            foreach (var observer in _observers)
            {
                try
                {
                    await observer.OnCompletedAsync(request, result, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Observers are extension points (memory, analytics). They must not fail the actor's decision.
                    _ = ex;
                }
            }
        }
    }
}
