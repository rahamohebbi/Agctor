using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.CodeGraph.Intents
{
    /// <summary>
    /// Fast, regex/keyword-based fallback for understanding common structural queries.
    /// </summary>
    public sealed class HeuristicIntentResolver : IIntentResolver
    {
        public IntentResolution Resolve(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return IntentResolution.Unresolved;
            prompt = prompt.Trim();
            var lowered = prompt.ToLowerInvariant();

            // list classes / files
            if (lowered.StartsWith("list") && lowered.Contains("class"))
                return new IntentResolution(IntentKind.ListClasses, null);
            if (lowered.StartsWith("list") && lowered.Contains("file"))
                return new IntentResolution(IntentKind.ListFiles, null);

            // list methods of class X
            if (TryExtractListMethodsRequest(prompt, out var clsName))
                return new IntentResolution(IntentKind.ListMethods, new Dictionary<string, string>{{"ClassName", clsName}});

            // lines of code
            if (TryExtractLineCountRequest(prompt, out var target, out var scope))
            {
                return scope switch
                {
                    "class" => new IntentResolution(IntentKind.CountLinesClass, new Dictionary<string,string>{{"ClassName", target}}),
                    "file"  => new IntentResolution(IntentKind.CountLinesFile,  new Dictionary<string,string>{{"FileName", target}}),
                    _        => IntentResolution.Unresolved
                };
            }

            return IntentResolution.Unresolved;
        }

        // === helpers copied from legacy SearchAgent (made static for isolation) ===
        private static bool TryExtractListMethodsRequest(string prompt, out string className)
        {
            className = string.Empty;
            var lowered = prompt.ToLowerInvariant();
            if (!(lowered.StartsWith("list") || lowered.StartsWith("what"))) return false;
            if (!lowered.Contains("method")) return false;

            var tokens = prompt.Split(new[] { ' ', '?', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                if (tokens[i].Equals("in", StringComparison.OrdinalIgnoreCase) || tokens[i].Equals("of", StringComparison.OrdinalIgnoreCase))
                {
                    className = tokens[i + 1];
                    return true;
                }
            }
            return false;
        }

        private static bool TryExtractLineCountRequest(string prompt, out string targetName, out string scope)
        {
            targetName = string.Empty;
            scope = string.Empty;
            var lowered = prompt.ToLowerInvariant();
            if (!lowered.Contains("lines of code")) return false;

            var tokens = prompt.Split(new[] { ' ', '?', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                if (tokens[i + 1].Equals("class", StringComparison.OrdinalIgnoreCase))
                {
                    targetName = tokens[i];
                    scope = "class";
                    return true;
                }
                if (tokens[i + 1].Equals("file", StringComparison.OrdinalIgnoreCase))
                {
                    targetName = tokens[i];
                    scope = "file";
                    return true;
                }
            }
            return false;
        }
    }
} 