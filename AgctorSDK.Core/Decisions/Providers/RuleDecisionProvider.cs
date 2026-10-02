using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Decisions.Providers
{
    /// <summary>
    /// Decides only when the request already contains a deterministic answer:
    /// matching rules, a single option, explicit option weights, or metadata value marked deterministic.
    /// Everything else returns false from <see cref="CanHandle"/> so the router can pick Laya or an LLM.
    /// </summary>
    public sealed class RuleDecisionProvider : IDecisionProvider
    {
        private readonly RuleProviderOptions _options;

        public RuleDecisionProvider()
            : this(new RuleProviderOptions())
        {
        }

        public RuleDecisionProvider(RuleProviderOptions options)
        {
            _options = options ?? new RuleProviderOptions();
        }

        public string Name => DecisionFabricIds.Rules;

        public DecisionProviderProfile Profile => new DecisionProviderProfile
        {
            Name = Name,
            Role = DecisionEngineRole.Rules,
            IsLocal = true,
            ExpectedLatency = TimeSpan.FromMilliseconds(1),
            ExpectedCost = 0m,
            ExpectedAccuracy = 1d,
            Enabled = _options.Enabled
        };

        public bool IsAvailable => _options.Enabled;

        public bool CanHandle(DecisionRequest request)
        {
            return request != null && TryEvaluate(request, out _);
        }

        public Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!TryEvaluate(request, out var result))
            {
                throw new DecisionException("Rules cannot decide this request.", request.Id, Name);
            }

            return Task.FromResult(result);
        }

        private bool TryEvaluate(DecisionRequest request, out DecisionResult result)
        {
            result = null!;
            if (!_options.Enabled)
            {
                return false;
            }

            if (TryForcedValue(request, out result))
            {
                return true;
            }

            if (TryBestRule(request, out result))
            {
                return true;
            }

            if (request.Type == DecisionType.Choice && TryWeightedChoice(request, out result))
            {
                return true;
            }

            if (request.Type == DecisionType.Choice && request.Options is { Count: 1 })
            {
                result = Build(request, request.Options[0].Id, 1d, null);
                return true;
            }

            return false;
        }

        private bool TryForcedValue(DecisionRequest request, out DecisionResult result)
        {
            result = null!;
            if (request.Metadata == null || !DecisionSignals.HasFlag(request, "deterministic"))
            {
                return false;
            }

            if (!request.Metadata.TryGetValue("value", out var forced) || forced == null)
            {
                return false;
            }

            switch (request.Type)
            {
                case DecisionType.Choice:
                    result = Build(request, Convert.ToString(forced, CultureInfo.InvariantCulture) ?? string.Empty, 1d, null);
                    return true;
                case DecisionType.Score when TryDouble(forced, out var score):
                    result = Build(request, ClampScore(request, score), 1d, null);
                    return true;
                case DecisionType.Binary when TryBool(forced, out var flag):
                    result = Build(request, flag, 1d, null);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryBestRule(DecisionRequest request, out DecisionResult result)
        {
            result = null!;
            DecisionRule? best = null;
            foreach (var rule in ReadRules(request))
            {
                if (!Matches(request, rule) || !Fits(request.Type, rule))
                {
                    continue;
                }

                if (best == null || rule.Confidence > best.Confidence)
                {
                    best = rule;
                }
            }

            if (best == null)
            {
                return false;
            }

            var confidence = Clamp01(best.Confidence);
            switch (request.Type)
            {
                case DecisionType.Choice:
                    var selected = best.Select ?? string.Empty;
                    result = Build(request, selected, confidence, new Dictionary<string, double> { [selected] = confidence });
                    return true;
                case DecisionType.Score:
                    result = Build(request, ClampScore(request, best.Score ?? 0d), confidence, null);
                    return true;
                case DecisionType.Binary:
                    result = Build(request, best.Value ?? false, confidence, null);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryWeightedChoice(DecisionRequest request, out DecisionResult result)
        {
            result = null!;
            var options = request.Options;
            if (options == null || options.Count == 0)
            {
                return false;
            }

            var weights = new List<(string Id, double Weight)>(options.Count);
            foreach (var option in options)
            {
                if (option.Metadata == null ||
                    !option.Metadata.TryGetValue("weight", out var raw) ||
                    !TryDouble(raw, out var weight))
                {
                    return false;
                }

                weights.Add((option.Id, weight));
            }

            // Weights are supplied by the caller, so picking the max is arithmetic, not a judgment.
            var sum = weights.Sum(item => item.Weight);
            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in weights)
            {
                scores[item.Id] = sum == 0d ? 0d : item.Weight / sum;
            }

            var winner = weights.OrderByDescending(item => item.Weight).First();
            result = Build(request, winner.Id, 1d, scores);
            return true;
        }

        private static IEnumerable<DecisionRule> ReadRules(DecisionRequest request)
        {
            if (request.Constraints?.Rules != null)
            {
                foreach (var rule in request.Constraints.Rules)
                {
                    if (rule != null)
                    {
                        yield return rule;
                    }
                }
            }

            if (request.Metadata != null &&
                request.Metadata.TryGetValue("rules", out var raw) &&
                raw is IEnumerable<DecisionRule> extra)
            {
                foreach (var rule in extra)
                {
                    if (rule != null)
                    {
                        yield return rule;
                    }
                }
            }
        }

        private static bool Fits(DecisionType type, DecisionRule rule)
        {
            return type switch
            {
                DecisionType.Choice => !string.IsNullOrWhiteSpace(rule.Select),
                DecisionType.Score => rule.Score.HasValue,
                DecisionType.Binary => rule.Value.HasValue,
                _ => false
            };
        }

        private static bool Matches(DecisionRequest request, DecisionRule rule)
        {
            if (string.IsNullOrWhiteSpace(rule.Field))
            {
                return true;
            }

            if (!TryReadField(request, rule.Field, out var actual))
            {
                return false;
            }

            return ValuesEqual(actual, rule.EqualsValue);
        }

        private static bool TryReadField(DecisionRequest request, string field, out object? value)
        {
            var state = request.State;
            if (state is IDictionary<string, object> typed && typed.TryGetValue(field, out value))
            {
                return true;
            }

            if (state is IDictionary dictionary && dictionary.Contains(field))
            {
                value = dictionary[field];
                return true;
            }

            if (state is JsonElement element &&
                element.ValueKind == JsonValueKind.Object &&
                TryJsonProperty(element, field, out value))
            {
                return true;
            }

            if (state != null)
            {
                var property = state.GetType().GetProperty(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (property != null)
                {
                    value = property.GetValue(state);
                    return true;
                }
            }

            if (request.Metadata != null && request.Metadata.TryGetValue(field, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryJsonProperty(JsonElement element, string field, out object? value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase))
                {
                    value = Unwrap(property.Value);
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static object? Unwrap(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var whole) => whole,
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        private static bool ValuesEqual(object? actual, object? expected)
        {
            if (actual is JsonElement element)
            {
                actual = Unwrap(element);
            }

            if (expected is JsonElement expectedElement)
            {
                expected = Unwrap(expectedElement);
            }

            if (actual == null || expected == null)
            {
                return actual == null && expected == null;
            }

            if (actual.Equals(expected))
            {
                return true;
            }

            if (TryDouble(actual, out var left) && TryDouble(expected, out var right))
            {
                return left.Equals(right);
            }

            return string.Equals(
                Convert.ToString(actual, CultureInfo.InvariantCulture),
                Convert.ToString(expected, CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
        }

        private DecisionResult Build(DecisionRequest request, object? value, double confidence, Dictionary<string, double>? scores)
        {
            return new DecisionResult
            {
                DecisionId = request.Id,
                Provider = Name,
                Value = value,
                Confidence = confidence,
                Scores = scores,
                Cost = 0m
            };
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

        private static double Clamp01(double value)
        {
            return Math.Clamp(value, 0d, 1d);
        }

        internal static bool TryDouble(object? value, out double number)
        {
            switch (value)
            {
                case double d:
                    number = d;
                    return true;
                case float f:
                    number = f;
                    return true;
                case int i:
                    number = i;
                    return true;
                case long l:
                    number = l;
                    return true;
                case decimal m:
                    number = (double)m;
                    return true;
                case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var jsonNumber):
                    number = jsonNumber;
                    return true;
                case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    number = parsed;
                    return true;
                default:
                    number = 0d;
                    return false;
            }
        }

        private static bool TryBool(object? value, out bool flag)
        {
            switch (value)
            {
                case bool b:
                    flag = b;
                    return true;
                case JsonElement element when element.ValueKind == JsonValueKind.True:
                    flag = true;
                    return true;
                case JsonElement element when element.ValueKind == JsonValueKind.False:
                    flag = false;
                    return true;
                case string text when bool.TryParse(text, out var parsed):
                    flag = parsed;
                    return true;
                default:
                    flag = false;
                    return false;
            }
        }
    }
}
