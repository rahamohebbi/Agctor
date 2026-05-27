using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AgctorSDK.Core.Ollama;

/// <summary>Calls Ollama <c>/api/chat</c> with vision model fallbacks.</summary>
public sealed class OllamaVisionChatClient : IOllamaVisionChatClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaVisionChatClient> _logger;

    public OllamaVisionChatClient(HttpClient http, ILogger<OllamaVisionChatClient> logger)
    {
        _http = http;
        _logger = logger;
        var timeoutSec = OllamaRuntimeConfiguration.GetVisionTimeoutSeconds();
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(30, timeoutSec));
    }

    public async Task<OllamaVisionChatResult> ChatAsync(
        string systemPrompt,
        string userText,
        IReadOnlyList<string> base64Images,
        int? numPredict = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = OllamaRuntimeConfiguration.GetApiUrlWithTrailingSlash();
        var models = OllamaRuntimeConfiguration.GetVisionModelCandidates();
        if (models.Length == 0)
        {
            return new OllamaVisionChatResult
            {
                Success = false,
                Error = "No vision model configured."
            };
        }

        var messages = new List<OllamaVisionChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new OllamaVisionChatMessage
            {
                Role = "system",
                Content = systemPrompt.Trim()
            });
        }

        messages.Add(new OllamaVisionChatMessage
        {
            Role = "user",
            Content = userText ?? "",
            Images = base64Images?.Count > 0 ? base64Images.ToList() : null
        });

        string? lastError = null;
        foreach (var model in models)
        {
            var body = new OllamaVisionChatRequest
            {
                Model = model,
                Stream = false,
                // Gemma 4 defaults to thinking mode; low num_predict then yields empty content.
                Think = false,
                Messages = messages,
                Options = numPredict is > 0
                    ? new OllamaVisionChatOptions { NumPredict = numPredict }
                    : null
            };

            try
            {
                using var response = await _http
                    .PostAsJsonAsync(baseUrl + "api/chat", body, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    lastError = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogWarning("Ollama vision chat failed for model {Model}: {Status} {Body}",
                        model, response.StatusCode, lastError);
                    continue;
                }

                var payload = await response.Content
                    .ReadFromJsonAsync<OllamaVisionChatResponse>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(payload?.Error))
                {
                    lastError = payload.Error;
                    continue;
                }

                var content = ResolveVisionText(payload?.Message);
                if (string.IsNullOrWhiteSpace(content))
                {
                    lastError = "Empty vision model response (model may need think=false or higher num_predict).";
                    continue;
                }

                return new OllamaVisionChatResult
                {
                    Success = true,
                    Content = content,
                    ModelUsed = model
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                lastError = ex.Message;
                _logger.LogWarning(ex, "Ollama vision chat error for model {Model}", model);
            }
        }

        return new OllamaVisionChatResult
        {
            Success = false,
            Error = lastError ?? "Vision chat failed for all configured models."
        };
    }

    /// <summary>Prefer final content; fall back to stripped thinking when models ignore think=false.</summary>
    private static string ResolveVisionText(OllamaVisionChatMessage? message)
    {
        if (message == null)
            return "";

        var content = OllamaThinkBlockStripper.Strip(message.Content ?? "");
        if (!string.IsNullOrWhiteSpace(content))
            return content;

        return OllamaThinkBlockStripper.Strip(message.Thinking ?? "");
    }
}
