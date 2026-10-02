using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Decisions.Providers
{
    /// <summary>
    /// Translates a decision into Laya's HTTP API and reads the response back into <see cref="DecisionResult"/>.
    /// The endpoint is configured; tests supply an <see cref="HttpClient"/> with a stub handler.
    /// </summary>
    public sealed class LayaDecisionProvider : IDecisionProvider
    {
        public const string HttpClientName = "agctor.decisions.laya";

        private readonly HttpClient _http;
        private readonly LayaProviderOptions _options;

        public LayaDecisionProvider(HttpClient httpClient, LayaProviderOptions? options = null)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options ?? new LayaProviderOptions();
        }

        public string Name => DecisionFabricIds.Laya;

        public DecisionProviderProfile Profile => new DecisionProviderProfile
        {
            Name = Name,
            Role = DecisionEngineRole.Laya,
            IsLocal = _options.IsLocal,
            ExpectedLatency = TimeSpan.FromMilliseconds(Math.Max(0, _options.ExpectedLatencyMs)),
            ExpectedCost = _options.ExpectedCost,
            ExpectedAccuracy = _options.ExpectedAccuracy,
            Enabled = _options.Enabled
        };

        public bool IsAvailable => _options.Enabled && Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out _);

        public bool CanHandle(DecisionRequest request)
        {
            // Laya can score, choose, or answer yes/no. The router still prefers the LLM when the request is complex.
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
                throw new DecisionException("Laya is not available.", request.Id, Name);
            }

            var payload = new Dictionary<string, object?>
            {
                ["id"] = request.Id,
                ["type"] = request.Type.ToString().ToLowerInvariant(),
                ["question"] = request.Question,
                ["state"] = request.State,
                ["options"] = request.Options,
                ["min"] = request.Constraints?.Min,
                ["max"] = request.Constraints?.Max
            };

            var uri = Combine(_options.Endpoint, _options.DecidePath);
            using var message = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, DecisionJson.Options), Encoding.UTF8, "application/json")
            };

            try
            {
                using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
                var body = response.Content == null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new DecisionException(
                        $"Laya returned {(int)response.StatusCode}.",
                        request.Id,
                        Name);
                }

                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                return DecisionJson.Read(request, Name, document.RootElement, _options.ExpectedCost);
            }
            catch (DecisionException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DecisionException("Laya request failed.", request.Id, Name, ex);
            }
        }

        internal static Uri Combine(string endpoint, string path)
        {
            var root = endpoint.TrimEnd('/');
            var relative = string.IsNullOrWhiteSpace(path) ? "/v1/decisions" : path;
            if (!relative.StartsWith("/", StringComparison.Ordinal))
            {
                relative = "/" + relative;
            }

            return new Uri(root + relative, UriKind.Absolute);
        }
    }
}
