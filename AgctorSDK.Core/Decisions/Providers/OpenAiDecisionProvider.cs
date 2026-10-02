using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Decisions.Providers
{
    /// <summary>
    /// LLM adapter that speaks the OpenAI chat-completions API.
    /// Swap <see cref="OpenAiProviderOptions.Endpoint"/> and <see cref="OpenAiProviderOptions.Name"/> for another
    /// OpenAI-shaped gateway; a different wire protocol would be a new <see cref="IDecisionProvider"/>.
    /// The prompt asks for a short summary only — chain-of-thought is not requested or stored.
    /// </summary>
    public sealed class OpenAiDecisionProvider : IDecisionProvider
    {
        public const string HttpClientName = "agctor.decisions.openai";

        private const string SystemPrompt =
            "You are a decision engine. Reply with one JSON object only, no markdown and no chain of thought. " +
            "Fields: value (string option id, number score, or boolean), confidence (0 to 1), " +
            "scores (object of option id to number, optional), summary (one short sentence, optional).";

        private readonly HttpClient _http;
        private readonly OpenAiProviderOptions _options;

        public OpenAiDecisionProvider(HttpClient httpClient, OpenAiProviderOptions? options = null)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options ?? new OpenAiProviderOptions();
        }

        public string Name => string.IsNullOrWhiteSpace(_options.Name) ? DecisionFabricIds.OpenAI : _options.Name;

        public DecisionProviderProfile Profile => new DecisionProviderProfile
        {
            Name = Name,
            Role = DecisionEngineRole.Llm,
            IsLocal = _options.IsLocal,
            ExpectedLatency = TimeSpan.FromMilliseconds(Math.Max(0, _options.ExpectedLatencyMs)),
            ExpectedCost = _options.ExpectedCost,
            ExpectedAccuracy = _options.ExpectedAccuracy,
            Enabled = _options.Enabled
        };

        public bool IsAvailable => _options.Enabled && Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out _);

        public bool CanHandle(DecisionRequest request)
        {
            return request != null && IsAvailable;
        }

        public async Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!IsAvailable)
            {
                throw new DecisionException("LLM provider is not available.", request.Id, Name);
            }

            var userPayload = new Dictionary<string, object?>
            {
                ["type"] = request.Type.ToString().ToLowerInvariant(),
                ["question"] = request.Question,
                ["options"] = request.Options,
                ["state"] = request.State,
                ["min"] = request.Constraints?.Min,
                ["max"] = request.Constraints?.Max
            };

            var body = new Dictionary<string, object?>
            {
                ["model"] = _options.Model,
                ["temperature"] = 0,
                ["messages"] = new object[]
                {
                    new Dictionary<string, string> { ["role"] = "system", ["content"] = SystemPrompt },
                    new Dictionary<string, string>
                    {
                        ["role"] = "user",
                        ["content"] = JsonSerializer.Serialize(userPayload, DecisionJson.Options)
                    }
                }
            };

            if (_options.UseJsonResponseFormat)
            {
                body["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" };
            }

            var uri = LayaDecisionProvider.Combine(_options.Endpoint, _options.ChatPath);
            using var message = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, DecisionJson.Options), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            try
            {
                using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
                var responseText = response.Content == null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new DecisionException($"LLM provider returned {(int)response.StatusCode}.", request.Id, Name);
                }

                var content = ExtractContent(responseText);
                using var document = JsonDocument.Parse(content);
                var result = DecisionJson.Read(request, Name, document.RootElement, _options.ExpectedCost);
                return result;
            }
            catch (DecisionException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DecisionException("LLM request failed.", request.Id, Name, ex);
            }
        }

        /// <summary>
        /// Chat completions wrap the decision JSON in choices[0].message.content. Some gateways also add fences.
        /// </summary>
        internal static string ExtractContent(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return "{}";
            }

            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    return StripFences(content.GetString() ?? "{}");
                }
            }

            // Already a bare decision document.
            return responseText;
        }

        private static string StripFences(string content)
        {
            var trimmed = content.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline < 0)
            {
                return trimmed;
            }

            trimmed = trimmed.Substring(firstNewline + 1);
            var fence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                trimmed = trimmed.Substring(0, fence);
            }

            return trimmed.Trim();
        }
    }
}
