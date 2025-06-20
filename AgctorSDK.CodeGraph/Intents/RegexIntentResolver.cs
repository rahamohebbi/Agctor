using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AgctorSDK.CodeGraph.Intents
{
    /// <summary>
    /// Intent resolver based purely on configurable regular expressions.
    /// Useful for deterministic mappings or unit tests without LLM.
    /// </summary>
    public sealed class RegexIntentResolver : IIntentResolver
    {
        private readonly List<(Regex pattern, IntentKind kind, string? slotKey)> _rules = new();

        public RegexIntentResolver()
        {
            // default rules comparable to heuristic resolver
            Add(@"\blist\s+classes?\b", IntentKind.ListClasses);
            Add(@"\blist\s+files?\b", IntentKind.ListFiles);
            Add(@"\blist\s+(?:all\s+)?methods?\s+(?:in|of)\s+(?:the\s+)?(?<name>\w+)(?:\s+class)?", IntentKind.ListMethods, "ClassName");
            Add(@"(?<name>\w+)\s+lines\s+of\s+code\s+in\s+class", IntentKind.CountLinesClass, "ClassName");
            Add(@"(?<name>\w+\.\w+)\s+lines\s+of\s+code", IntentKind.CountLinesFile, "FileName");
            Add(@"\bshow\s+code\s+for\s+(?<name>\w+)\s+method\b", IntentKind.GetMethodSource, "MethodName");
            Add(@"\b(show|tell)\s+(me\s+)?(about\s+)?(?<name>\w+)\s+method\b", IntentKind.GetMethodSource, "MethodName");
            Add(@"\bshow\s+code\s+for\s+(?<name>\w+)\s+class\b", IntentKind.GetClassSource, "ClassName");
            Add(@"\b(show|tell)\s+(me\s+)?(about\s+)?(?<name>\w+)\s+class\b", IntentKind.GetClassSource, "ClassName");
        }

        public void Add(string pattern, IntentKind kind, string? slotKey = null)
        {
            _rules.Add((new Regex(pattern, RegexOptions.IgnoreCase), kind, slotKey));
        }

        public IntentResolution Resolve(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return IntentResolution.Unresolved;

            // Replace various non-standard whitespace chars with regular spaces and collapse multiples.
            var normalized = Regex.Replace(
                prompt.Replace('\u00A0', ' ')   // NBSP
                      .Replace('\u2009', ' ')   // thin space
                      .Replace('\u202F', ' '),  // NNBSP
                @"\s+", " ", RegexOptions.CultureInvariant);

            foreach (var (regex, kind, slotKey) in _rules)
            {
                var m = regex.Match(normalized);
                if (!m.Success) continue;
                Dictionary<string,string>? slots = null;
                if (slotKey != null)
                {
                    var val = m.Groups["name"].Success ? m.Groups["name"].Value : null;
                    if (val != null)
                        slots = new Dictionary<string, string>{{slotKey, val}};
                }
                return new IntentResolution(kind, slots);
            }
            return IntentResolution.Unresolved;
        }
    }
} 