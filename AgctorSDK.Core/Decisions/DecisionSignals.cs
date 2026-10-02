using System;
using System.Globalization;

namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Shared checks for "this request needs reasoning" so the router and providers agree.
    /// </summary>
    public static class DecisionSignals
    {
        public static bool IsComplex(DecisionRequest request, DecisionRoutingOptions? routing = null)
        {
            if (request == null)
            {
                return false;
            }

            if (HasFlag(request, "requiresReasoning"))
            {
                return true;
            }

            if (MetadataEquals(request, "complexity", "complex") || MetadataEquals(request, "complexity", "high"))
            {
                return true;
            }

            var questionLimit = routing?.ComplexQuestionLength ?? 280;
            var optionLimit = routing?.ComplexOptionCount ?? 8;

            if ((request.Question?.Length ?? 0) >= questionLimit)
            {
                return true;
            }

            if ((request.Options?.Count ?? 0) >= optionLimit)
            {
                return true;
            }

            return false;
        }

        public static bool HasFlag(DecisionRequest request, string key)
        {
            if (request.Metadata == null || !request.Metadata.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            if (raw is bool flag)
            {
                return flag;
            }

            var text = Convert.ToString(raw, CultureInfo.InvariantCulture);
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
        }

        public static bool MetadataEquals(DecisionRequest request, string key, string expected)
        {
            if (request.Metadata == null || !request.Metadata.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            var text = Convert.ToString(raw, CultureInfo.InvariantCulture);
            return string.Equals(text, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
