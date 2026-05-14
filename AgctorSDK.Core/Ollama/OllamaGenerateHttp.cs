using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Streaming;

namespace AgctorSDK.Core.Ollama;

/// <summary>
/// Single implementation for Ollama <c>/api/generate</c> (non-stream and NDJSON stream parsing).
/// Used by LLMAgent, project-memory completion, and Host playground SSE.
/// </summary>
public static class OllamaGenerateHttp
{
    /// <summary>POST non-streaming generate; caller must dispose the response.</summary>
    public static Task<HttpResponseMessage> SendNonStreamingGenerateAsync(
        HttpClient http,
        string ollamaBaseWithTrailingSlash,
        string model,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var body = new OllamaGenerateRequest { Model = model, Prompt = prompt, Stream = false };
        return http.PostAsJsonAsync(ollamaBaseWithTrailingSlash + "api/generate", body, cancellationToken);
    }

    /// <summary>POST streaming generate; caller must dispose the response.</summary>
    public static async Task<HttpResponseMessage> SendStreamingGenerateAsync(
        HttpClient http,
        string ollamaBaseWithTrailingSlash,
        string model,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var body = new OllamaGenerateRequest { Model = model, Prompt = prompt, Stream = true };
        using var req = new HttpRequestMessage(HttpMethod.Post, ollamaBaseWithTrailingSlash + "api/generate")
        {
            Content = JsonContent.Create(body)
        };
        return await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Interpret a completed non-streaming HTTP response (success or error status).</summary>
    public static async Task<OllamaNonStreamingInterpretation> InterpretNonStreamingResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return OllamaNonStreamingInterpretation.Failure(
                $"Error: Ollama API request failed with status {response.StatusCode}. Details: {errorContent}",
                "OllamaApiError");
        }

        OllamaGenerateResponse? ollamaResponse;
        try
        {
            ollamaResponse = await response.Content
                .ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException jx)
        {
            return OllamaNonStreamingInterpretation.Failure(
                $"Error: Failed to parse Ollama response. {jx.Message}",
                "OllamaJsonError");
        }

        if (ollamaResponse != null && ollamaResponse.Done)
        {
            return OllamaNonStreamingInterpretation.Success(
                (ollamaResponse.Response ?? string.Empty).Trim(),
                "LLMResponse");
        }

        var responseText = ollamaResponse?.Response ?? "no response text";
        var errorDetail = ollamaResponse == null
            ? "null response object"
            : $"done flag is {ollamaResponse.Done}, response text: {responseText}";
        return OllamaNonStreamingInterpretation.Failure(
            $"Error: Ollama did not return a final response. Detail: {errorDetail}",
            "OllamaIncompleteResponseError");
    }

    /// <summary>Send + interpret non-streaming generate (throws on failure — same strictness as LLMAgent success path).</summary>
    public static async Task<string> GenerateNonStreamingTextAsync(
        HttpClient http,
        string ollamaBaseWithTrailingSlash,
        string model,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendNonStreamingGenerateAsync(http, ollamaBaseWithTrailingSlash, model, prompt, cancellationToken)
            .ConfigureAwait(false);
        var tri = await InterpretNonStreamingResponseAsync(response, cancellationToken).ConfigureAwait(false);
        if (!tri.Ok)
            throw new InvalidOperationException(tri.Payload);
        return tri.Text ?? string.Empty;
    }

    /// <summary>Read NDJSON stream from Ollama; optional per-token callback (e.g. SSE).</summary>
    public static async Task<OllamaStreamAccumulation> ConsumeNdjsonGenerateStreamAsync(
        Stream body,
        Func<string, CancellationToken, ValueTask>? onToken,
        CancellationToken cancellationToken = default)
    {
        var acc = new StringBuilder();
        // Caller owns the stream (e.g. HttpContent); do not close it here.
        using var reader = new StreamReader(body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024,
            leaveOpen: true);
        var sawDone = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
                break;

            if (!OllamaStreamLineParser.TryParseLine(line, out var token, out var done))
                continue;

            if (!string.IsNullOrEmpty(token))
            {
                acc.Append(token);
                if (onToken != null)
                    await onToken(token, cancellationToken).ConfigureAwait(false);
            }

            if (done)
            {
                sawDone = true;
                break;
            }
        }

        if (!sawDone && acc.Length == 0)
            return new OllamaStreamAccumulation(string.Empty, "Error: Ollama stream ended without a final chunk.");
        if (!sawDone)
            return new OllamaStreamAccumulation(acc.ToString(), "Error: Ollama stream ended before done flag.");
        return new OllamaStreamAccumulation(acc.ToString(), null);
    }

    /// <summary>POST streaming generate and accumulate tokens (optional per-token callback).</summary>
    public static async Task<OllamaStreamAccumulation> StreamGenerateAsync(
        HttpClient http,
        string ollamaBaseWithTrailingSlash,
        string model,
        string prompt,
        Func<string, CancellationToken, ValueTask>? onToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendStreamingGenerateAsync(http, ollamaBaseWithTrailingSlash, model, prompt, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ConsumeNdjsonGenerateStreamAsync(stream, onToken, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Outcome of interpreting a non-streaming Ollama response.</summary>
public readonly record struct OllamaNonStreamingInterpretation(bool Ok, string? Text, string? Payload, string MessageType)
{
    public static OllamaNonStreamingInterpretation Success(string text, string messageType) =>
        new(true, text, null, messageType);

    public static OllamaNonStreamingInterpretation Failure(string payload, string messageType) =>
        new(false, null, payload, messageType);
}

/// <summary>Result of reading a streaming Ollama body.</summary>
public readonly record struct OllamaStreamAccumulation(string Text, string? Error);
