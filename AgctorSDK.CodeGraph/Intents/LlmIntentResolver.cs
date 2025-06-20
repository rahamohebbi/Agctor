using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Llm;

namespace AgctorSDK.CodeGraph.Intents
{
    /// <summary>
    /// Uses an LLM to classify the search prompt into an intent.
    /// Returns Unresolved if LLM call fails or produces invalid JSON.
    /// </summary>
    public sealed class LlmIntentResolver : IIntentResolver
    {
        private readonly ILlmClient _client;
        private readonly LlmOptions _options;

        public LlmIntentResolver(ILlmClient client, LlmOptions? options = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? new LlmOptions();
        }

        public IntentResolution Resolve(string prompt)
        {
            try
            {
                var task = ResolveAsync(prompt);
                task.Wait();
                return task.Result;
            }
            catch
            {
                return IntentResolution.Unresolved;
            }
        }

        private async Task<IntentResolution> ResolveAsync(string prompt)
        {
            string systemPrompt = "You are an assistant that classifies developer code-search queries. " +
                                  "Respond ONLY with JSON matching this C# record: { \"intent\": string, \"slots\": object }. " +
                                  "intent must be one of: list_classes, list_files, list_methods, count_lines_class, count_lines_file, semantic_search. " +
                                  "slots is an object with additional keys (e.g., ClassName, FileName) or {} when not needed.";

            string fullPrompt = systemPrompt + "\nQuery: " + prompt + "\nJSON:";

            var raw = await _client.CompleteAsync(fullPrompt, _options);
            try
            {
                var doc = JsonSerializer.Deserialize<LlmIntentDto>(raw, new JsonSerializerOptions{PropertyNameCaseInsensitive = true});
                if (doc == null || string.IsNullOrEmpty(doc.Intent))
                    return IntentResolution.Unresolved;

                var intentKind = doc.Intent.ToLowerInvariant() switch
                {
                    "list_classes"       => IntentKind.ListClasses,
                    "list_files"         => IntentKind.ListFiles,
                    "list_methods"       => IntentKind.ListMethods,
                    "count_lines_class"  => IntentKind.CountLinesClass,
                    "count_lines_file"   => IntentKind.CountLinesFile,
                    "semantic_search"    => IntentKind.SemanticSearch,
                    _ => IntentKind.Unknown
                };

                if (intentKind == IntentKind.Unknown) return IntentResolution.Unresolved;
                var slots = doc.Slots ?? new Dictionary<string,string>();
                return new IntentResolution(intentKind, slots);
            }
            catch
            {
                return IntentResolution.Unresolved;
            }
        }

        private sealed class LlmIntentDto
        {
            [JsonPropertyName("intent")] public string Intent { get; init; } = string.Empty;
            [JsonPropertyName("slots")]  public Dictionary<string,string>? Slots { get; init; }
        }
    }
} 