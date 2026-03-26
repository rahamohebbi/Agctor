using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces; // For IActor, ActorState, IMessageEnvelope, IMessageMetadata, ActorStateChangedEventArgs
using AgctorSDK.Core.Messages;   // For MessageEnvelope
using AgctorSDK.Core.Streaming;
// If IActor and other interfaces are in a sub-namespace like Agctor.Core.Interfaces, add that too.
// For now, assuming IActor is also directly under Agctor.Core based on its previous colocation.

namespace AgctorSDK.Core.Agents
{
    // Ollama Specific Classes
    public class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } // false = single JSON; true = NDJSON lines (PRD-011)
    }

    public class OllamaGenerateResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("response")]
        public string Response { get; set; } = default!;

        [JsonPropertyName("done")]
        public bool Done { get; set; }
        
        // Other fields like context, durations, etc., are ignored for simplicity for now.
    }

    /// <summary>
    /// LLMAgent that communicates with a local Ollama instance.
    /// As per PRD: prd-core-001.md
    /// </summary>
    public class LLMAgent : Agent // Inherit from Agent instead of IActor
    {
        private const string DefaultOllamaApiUrl = "http://localhost:11434";
        private const string DefaultModel = "mistral";
        private static readonly object DefaultsLock = new();
        private static string _configuredOllamaApiUrl = DefaultOllamaApiUrl;
        private static string _configuredDefaultModel = DefaultModel;

        private readonly HttpClient _httpClient;
        private readonly string _ollamaApiUrl;
        private readonly string _defaultModel;

        public LLMAgent(string id) : this(id, CreateClient(), GetConfiguredOllamaApiUrl(), GetConfiguredDefaultModel())
        {
        }

        public LLMAgent(string id, HttpClient httpClient, string ollamaApiUrl = DefaultOllamaApiUrl, string defaultModel = DefaultModel)
            : base(id)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(180); // Allow larger generations
            _ollamaApiUrl = ollamaApiUrl.EndsWith("/") ? ollamaApiUrl : ollamaApiUrl + "/";
            _defaultModel = defaultModel;
        }

        public LLMAgent(string id, string ollamaApiUrl = DefaultOllamaApiUrl, string defaultModel = DefaultModel)
            : this(id, CreateClient(), ollamaApiUrl, defaultModel)
        {
        }

        /// <summary>
        /// Configures LLMAgent defaults used by the single-argument constructor.
        /// Intended to be called once at host startup from configuration.
        /// </summary>
        public static void ConfigureDefaults(string? ollamaApiUrl, string? defaultModel)
        {
            lock (DefaultsLock)
            {
                _configuredOllamaApiUrl = string.IsNullOrWhiteSpace(ollamaApiUrl)
                    ? DefaultOllamaApiUrl
                    : ollamaApiUrl.Trim();
                _configuredDefaultModel = string.IsNullOrWhiteSpace(defaultModel)
                    ? DefaultModel
                    : defaultModel.Trim();
            }
        }

        public static string GetConfiguredOllamaApiUrl()
        {
            lock (DefaultsLock)
            {
                return _configuredOllamaApiUrl;
            }
        }

        public static string GetConfiguredDefaultModel()
        {
            lock (DefaultsLock)
            {
                return _configuredDefaultModel;
            }
        }

        private static HttpClient CreateClient()
        {
            var cli = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
            return cli;
        }

        public override async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Use cancellationToken if applicable for the GetAsync call
                var response = await _httpClient.GetAsync(_ollamaApiUrl + "api/tags", cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Warning: LLMAgent ({Id}) could not connect to Ollama at {_ollamaApiUrl} during initialization. Status: {response.StatusCode}");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Warning: LLMAgent ({Id}) HttpRequestException during initialization for Ollama at {_ollamaApiUrl}: {ex.Message}");
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested) // Handle cancellation
            {
                Console.WriteLine($"LLMAgent ({Id}) InitializeAsync was canceled: {ex.Message}");
                // Decide if state should change or rethrow, for now, it proceeds to Active or Faulted if it was before cancellation.
            }

            await base.InitializeAsync(cancellationToken);
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken)
        {
            string? requestSenderId = null;
            string? requestCorrelationId = null;

            if (envelope?.Headers?.TryGetValue("SenderId", out var sid) == true) requestSenderId = sid;
            if (envelope?.Metadata?.TryGetValue("CorrelationId", out var corrIdObj) == true && corrIdObj is string corrIdStr) requestCorrelationId = corrIdStr;

            var responseMetadata = new Dictionary<string, object>
            {
                { "Timestamp", DateTimeOffset.UtcNow }
            };
            if (requestCorrelationId != null) responseMetadata["CorrelationId"] = requestCorrelationId;

            var responseHeaders = new Dictionary<string, string>
            {
                { "SenderId", Id },
                { "ReceiverId", requestSenderId ?? "unknown" }, // Default to unknown if not present
                { "Version", "1.0" }
            };

            if (State != ActorState.Active)
            {
                Console.WriteLine($"LLMAgent ({Id}) received message while not active. State: {State}");
                responseHeaders["MessageType"] = "AgentNotActiveError";
                return new MessageEnvelope(
                    payload: $"Agent not active. Current state: {State}",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }

            if (envelope.Payload is not string prompt)
            {
                Console.WriteLine($"LLMAgent ({Id}) received invalid prompt payload type: {envelope.Payload?.GetType().Name}");
                responseHeaders["MessageType"] = "InvalidPromptError";
                return new MessageEnvelope(
                    payload: "Error: Prompt must be a non-empty string.",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }

            if (string.IsNullOrWhiteSpace(prompt) || int.TryParse(prompt, out _))
            {
                Console.WriteLine($"LLMAgent ({Id}) received empty or invalid prompt string.");
                responseHeaders["MessageType"] = "InvalidPromptError";
                return new MessageEnvelope(
                    payload: "Error: Prompt must be a non-empty string.",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }

            try
            {
                var requestPayloadOllama = new OllamaGenerateRequest
                {
                    Model = _defaultModel,
                    Prompt = prompt,
                    Stream = false
                };

                var streamId = TryGetHeaderInsensitive(envelope.Headers, AgentStreamHeaders.StreamId);
                var traceForStream = TryGetHeaderInsensitive(envelope.Headers, "trace-id");

                HttpResponseMessage httpResponse;
                try
                {
                    if (!string.IsNullOrWhiteSpace(streamId))
                    {
                        requestPayloadOllama.Stream = true;
                        using var req = new HttpRequestMessage(HttpMethod.Post, _ollamaApiUrl + "api/generate")
                        {
                            Content = JsonContent.Create(requestPayloadOllama)
                        };
                        httpResponse = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    }
                    else
                    {
                        httpResponse = await _httpClient.PostAsJsonAsync(
                            _ollamaApiUrl + "api/generate",
                            requestPayloadOllama,
                            cancellationToken);
                    }
                }
                catch (TaskCanceledException tex) when (!cancellationToken.IsCancellationRequested)
                {
                    // HttpClient timeout
                    PublishIfStreaming(streamId, traceForStream, new AgentStreamEvent { Type = "error", Payload = "LLM request timed out before completion." });
                    responseHeaders["MessageType"] = "OllamaTimeout";
                    return new MessageEnvelope(
                        payload: "Error: LLM request timed out before completion.",
                        metadata: responseMetadata,
                        id: Guid.NewGuid().ToString(),
                        headers: responseHeaders);
                }

                if (httpResponse.IsSuccessStatusCode)
                {
                    if (!string.IsNullOrWhiteSpace(streamId))
                    {
                        var streamed = await ReadOllamaStreamAsync(httpResponse, streamId, traceForStream, cancellationToken);
                        if (streamed.Error != null)
                        {
                            responseHeaders["MessageType"] = "OllamaStreamError";
                            return new MessageEnvelope(
                                payload: streamed.Error,
                                metadata: responseMetadata,
                                id: Guid.NewGuid().ToString(),
                                headers: responseHeaders);
                        }

                        responseHeaders["MessageType"] = "LLMResponse";
                        PublishIfStreaming(streamId, traceForStream, new AgentStreamEvent { Type = "llm_done", Payload = streamed.FullText });
                        return new MessageEnvelope(
                            payload: streamed.FullText,
                            metadata: responseMetadata,
                            id: Guid.NewGuid().ToString(),
                            headers: responseHeaders);
                    }

                    var ollamaResponse = await httpResponse.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken);
                    if (ollamaResponse != null && ollamaResponse.Done)
                    {
                        responseHeaders["MessageType"] = "LLMResponse";
                        return new MessageEnvelope(
                            payload: ollamaResponse.Response,
                            metadata: responseMetadata,
                            id: Guid.NewGuid().ToString(),
                            headers: responseHeaders);
                    }
                    else
                    {
                        string responseText = ollamaResponse?.Response ?? "no response text";
                        string errorDetail = ollamaResponse == null ? "null response object" : $"done flag is {ollamaResponse.Done}, response text: {responseText}";
                        Console.WriteLine($"LLMAgent ({Id}) received incomplete or non-final response from Ollama: {errorDetail}");
                        responseHeaders["MessageType"] = "OllamaIncompleteResponseError";
                        return new MessageEnvelope(
                            payload: $"Error: Ollama did not return a final response. Detail: {errorDetail}",
                            metadata: responseMetadata,
                            id: Guid.NewGuid().ToString(),
                            headers: responseHeaders);
                    }
                }
                else
                {
                    string errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"LLMAgent ({Id}) error from Ollama API: {httpResponse.StatusCode}. Details: {errorContent}");
                    PublishIfStreaming(streamId, traceForStream, new AgentStreamEvent { Type = "error", Payload = $"Ollama HTTP {(int)httpResponse.StatusCode}: {errorContent}" });
                    responseHeaders["MessageType"] = "OllamaApiError";
                    return new MessageEnvelope(
                        payload: $"Error: Ollama API request failed with status {httpResponse.StatusCode}. Details: {errorContent}",
                        metadata: responseMetadata,
                        id: Guid.NewGuid().ToString(),
                        headers: responseHeaders);
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"LLMAgent ({Id}) HttpRequestException while communicating with Ollama: {ex.Message}");
                var hdrStreamId = TryGetHeaderInsensitive(envelope.Headers, AgentStreamHeaders.StreamId);
                var hdrTraceId = TryGetHeaderInsensitive(envelope.Headers, "trace-id");
                PublishIfStreaming(hdrStreamId, hdrTraceId, new AgentStreamEvent { Type = "error", Payload = $"Network error: {ex.Message}" });
                ChangeActorState(ActorState.Faulted);
                responseHeaders["MessageType"] = "OllamaHttpRequestError";
                return new MessageEnvelope(
                    payload: $"Error: Network communication with Ollama failed. {ex.Message}",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"LLMAgent ({Id}) JsonException while processing Ollama response: {ex.Message}");
                var hdrStreamId2 = TryGetHeaderInsensitive(envelope.Headers, AgentStreamHeaders.StreamId);
                var hdrTraceId2 = TryGetHeaderInsensitive(envelope.Headers, "trace-id");
                PublishIfStreaming(hdrStreamId2, hdrTraceId2, new AgentStreamEvent { Type = "error", Payload = $"Parse error: {ex.Message}" });
                responseHeaders["MessageType"] = "OllamaJsonError";
                return new MessageEnvelope(
                    payload: $"Error: Failed to parse Ollama response. {ex.Message}",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"LLMAgent ({Id}) task was canceled: {ex.Message}");
                responseHeaders["MessageType"] = "TaskCanceledError";
                return new MessageEnvelope(
                    payload: "Error: Task was canceled.",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LLMAgent ({Id}) an unexpected error occurred: {ex.Message}");
                ChangeActorState(ActorState.Faulted);
                responseHeaders["MessageType"] = "UnexpectedError";
                return new MessageEnvelope(
                    payload: $"Error: An unexpected error occurred. {ex.Message}",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }
        }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LogInfo($"LLMAgent processing prompt: {prompt}");
            ChangeAgentStatus(AgentStatus.Working, $"Processing prompt: {prompt}");

            try
            {
                // Create a message envelope for the prompt to pass to ReceiveAsync
                var promptEnvelope = new MessageEnvelope(prompt, new Dictionary<string, object>(), Id, new Dictionary<string, string>
                {
                    { "SenderId", ParentAgentId ?? "root" },
                    { "ReceiverId", Id }
                });

                // Use ReceiveAsync to get the LLM's response
                var resultEnvelope = await ReceiveAsync(promptEnvelope, cancellationToken);

                if (resultEnvelope.Payload is string resultPayload && !resultPayload.StartsWith("Error:"))
                {
                    LogInfo($"LLM response received: {resultPayload}");

                    // Check if the response is a tool call
                    if (resultPayload.Contains("CodeEditorTool"))
                    {
                        await AssignSubtaskAsync(resultPayload, "CodeEditorTool", cancellationToken);
                        ChangeAgentStatus(AgentStatus.WaitingForSubtasks, "Waiting for CodeEditorTool to complete.");
                    }
                    else
                    {
                        // If it's not a tool call, the task is considered complete
                        ChangeAgentStatus(AgentStatus.Completed, "Prompt processed directly by LLM.");
                        await FinalizeTask(resultPayload, cancellationToken);
                    }
                }
                else
                {
                    var error = resultEnvelope.Payload as string ?? "Unknown error from LLM.";
                    LogError($"Error processing prompt: {error}");
                    ChangeAgentStatus(AgentStatus.Failed, error);
                    await FinalizeTaskAsFailed(new Exception(error), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LogError($"An unexpected error occurred in ProcessPromptAsync: {ex.Message}");
                ChangeAgentStatus(AgentStatus.Failed, ex.Message);
                await FinalizeTaskAsFailed(ex, cancellationToken);
            }
        }

        private static string? TryGetHeaderInsensitive(IReadOnlyDictionary<string, string>? headers, string name)
        {
            if (headers == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (var kv in headers)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }

            return null;
        }

        private void PublishIfStreaming(string? streamId, string? traceId, AgentStreamEvent evt)
        {
            if (string.IsNullOrWhiteSpace(streamId))
            {
                return;
            }

            evt.TraceId ??= traceId;
            evt.AgentId ??= Id;
            AgentOutputStreamHub.Registry.Publish(streamId, evt);
        }

        private async Task<(string FullText, string? Error)> ReadOllamaStreamAsync(
            HttpResponseMessage httpResponse,
            string streamId,
            string? traceId,
            CancellationToken cancellationToken)
        {
            var acc = new StringBuilder();
            await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var sawDone = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null)
                {
                    break;
                }

                if (!OllamaStreamLineParser.TryParseLine(line, out var token, out var done))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(token))
                {
                    acc.Append(token);
                    PublishIfStreaming(streamId, traceId, new AgentStreamEvent { Type = "llm_delta", Payload = token });
                }

                if (done)
                {
                    sawDone = true;
                    break;
                }
            }

            if (!sawDone && acc.Length == 0)
            {
                return (string.Empty, "Error: Ollama stream ended without a final chunk.");
            }

            if (!sawDone)
            {
                return (acc.ToString(), "Error: Ollama stream ended before done flag.");
            }

            return (acc.ToString(), null);
        }

        public override async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            // Handle cancellation if any long-running shutdown operations are added
            if (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"LLMAgent ({Id}) ShutdownAsync was canceled.");
                // Potentially throw OperationCanceledException or handle gracefully
                cancellationToken.ThrowIfCancellationRequested();
            }
            ChangeActorState(ActorState.Stopping);
            ChangeActorState(ActorState.Stopped);
            await base.ShutdownAsync(cancellationToken);
        }
    }

    // The internal DefaultMessageMetadata class below will be removed.
    // internal class DefaultMessageMetadata : IMessageMetadata { ... }
} 