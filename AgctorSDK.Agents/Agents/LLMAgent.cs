using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces; // For IActor, ActorState, IMessageEnvelope, IMessageMetadata, ActorStateChangedEventArgs
using AgctorSDK.Core.Messages;   // For MessageEnvelope
using AgctorSDK.Core.Streaming;
using AgctorSDK.Core.Ollama;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// LLMAgent that communicates with a local Ollama instance.
    /// As per PRD: prd-core-001.md
    /// </summary>
    public class LLMAgent : Agent // Inherit from Agent instead of IActor
    {
        private const string DefaultOllamaApiUrl = "http://localhost:11434";
        private const string DefaultModel = "mistral";

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
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
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
        public static void ConfigureDefaults(string? ollamaApiUrl, string? defaultModel) =>
            OllamaRuntimeConfiguration.ConfigureDefaults(ollamaApiUrl, defaultModel);

        public static string GetConfiguredOllamaApiUrl() => OllamaRuntimeConfiguration.GetOllamaApiUrl();

        public static string GetConfiguredDefaultModel() => OllamaRuntimeConfiguration.GetDefaultModel();

        private static HttpClient CreateClient()
        {
            var cli = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
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

            if (envelope?.Headers?.TryGetValue(AgctorMessageHeaders.SenderId, out var sid) == true) requestSenderId = sid;
            if (envelope?.Metadata?.TryGetValue(AgctorMessageHeaders.CorrelationId, out var corrIdObj) == true && corrIdObj is string corrIdStr) requestCorrelationId = corrIdStr;

            var responseMetadata = new Dictionary<string, object>
            {
                { "Timestamp", DateTimeOffset.UtcNow }
            };
            if (requestCorrelationId != null) responseMetadata[AgctorMessageHeaders.CorrelationId] = requestCorrelationId;

            var responseHeaders = new Dictionary<string, string>
            {
                { AgctorMessageHeaders.SenderId, Id },
                { AgctorMessageHeaders.ReceiverId, requestSenderId ?? "unknown" }, // Default to unknown if not present
                { AgctorMessageHeaders.Version, AgctorEnvelopeBuilder.ProtocolVersion }
            };

            if (State != ActorState.Active)
            {
                Console.WriteLine($"LLMAgent ({Id}) received message while not active. State: {State}");
                responseHeaders[AgctorMessageHeaders.MessageType] = "AgentNotActiveError";
                return new MessageEnvelope(
                    payload: $"Agent not active. Current state: {State}",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }

            if (envelope.Payload is not string prompt)
            {
                Console.WriteLine($"LLMAgent ({Id}) received invalid prompt payload type: {envelope.Payload?.GetType().Name}");
                responseHeaders[AgctorMessageHeaders.MessageType] = "InvalidPromptError";
                return new MessageEnvelope(
                    payload: "Error: Prompt must be a non-empty string.",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }

            if (string.IsNullOrWhiteSpace(prompt) || int.TryParse(prompt, out _))
            {
                Console.WriteLine($"LLMAgent ({Id}) received empty or invalid prompt string.");
                responseHeaders[AgctorMessageHeaders.MessageType] = "InvalidPromptError";
                return new MessageEnvelope(
                    payload: "Error: Prompt must be a non-empty string.",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }

            try
            {
                var streamId = TryGetHeaderInsensitive(envelope.Headers, AgentStreamHeaders.StreamId);
                var traceForStream = TryGetHeaderInsensitive(envelope.Headers, "trace-id");

                try
                {
                    using var httpResponse = await (
                            !string.IsNullOrWhiteSpace(streamId)
                                ? OllamaGenerateHttp.SendStreamingGenerateAsync(
                                    _httpClient,
                                    _ollamaApiUrl,
                                    _defaultModel,
                                    prompt,
                                    cancellationToken)
                                : OllamaGenerateHttp.SendNonStreamingGenerateAsync(
                                    _httpClient,
                                    _ollamaApiUrl,
                                    _defaultModel,
                                    prompt,
                                    cancellationToken))
                        .ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(streamId))
                    {
                        if (!httpResponse.IsSuccessStatusCode)
                        {
                            var errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                            Console.WriteLine($"LLMAgent ({Id}) error from Ollama API: {httpResponse.StatusCode}. Details: {errorContent}");
                            PublishIfStreaming(streamId, traceForStream, new AgentStreamEvent { Type = "error", Payload = $"Ollama HTTP {(int)httpResponse.StatusCode}: {errorContent}" });
                            responseHeaders[AgctorMessageHeaders.MessageType] = "OllamaApiError";
                            return new MessageEnvelope(
                                payload: $"Error: Ollama API request failed with status {httpResponse.StatusCode}. Details: {errorContent}",
                                metadata: responseMetadata,
                                id: Guid.NewGuid().ToString(),
                                headers: responseHeaders);
                        }

                        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        var streamed = await OllamaGenerateHttp.ConsumeNdjsonGenerateStreamAsync(
                                stream,
                                (t, _) =>
                                {
                                    PublishIfStreaming(streamId, traceForStream, new AgentStreamEvent { Type = "llm_delta", Payload = t });
                                    return ValueTask.CompletedTask;
                                },
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (streamed.Error != null)
                        {
                            responseHeaders[AgctorMessageHeaders.MessageType] = "OllamaStreamError";
                            return new MessageEnvelope(
                                payload: streamed.Error,
                                metadata: responseMetadata,
                                id: Guid.NewGuid().ToString(),
                                headers: responseHeaders);
                        }

                        responseHeaders[AgctorMessageHeaders.MessageType] = "LLMResponse";
                        PublishIfStreaming(streamId, traceForStream, new AgentStreamEvent { Type = "llm_done", Payload = streamed.Text });
                        return new MessageEnvelope(
                            payload: streamed.Text,
                            metadata: responseMetadata,
                            id: Guid.NewGuid().ToString(),
                            headers: responseHeaders);
                    }

                    var tri = await OllamaGenerateHttp.InterpretNonStreamingResponseAsync(httpResponse, cancellationToken)
                        .ConfigureAwait(false);
                    if (!tri.Ok)
                    {
                        PublishIfStreaming(streamId, traceForStream, new AgentStreamEvent { Type = "error", Payload = tri.Payload ?? tri.MessageType });
                        responseHeaders[AgctorMessageHeaders.MessageType] = tri.MessageType;
                        return new MessageEnvelope(
                            payload: tri.Payload ?? "Error",
                            metadata: responseMetadata,
                            id: Guid.NewGuid().ToString(),
                            headers: responseHeaders);
                    }

                    responseHeaders[AgctorMessageHeaders.MessageType] = "LLMResponse";
                    return new MessageEnvelope(
                        payload: tri.Text ?? string.Empty,
                        metadata: responseMetadata,
                        id: Guid.NewGuid().ToString(),
                        headers: responseHeaders);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    PublishIfStreaming(streamId, traceForStream, new AgentStreamEvent { Type = "error", Payload = "LLM request timed out before completion." });
                    responseHeaders[AgctorMessageHeaders.MessageType] = "OllamaTimeout";
                    return new MessageEnvelope(
                        payload: "Error: LLM request timed out before completion.",
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
                responseHeaders[AgctorMessageHeaders.MessageType] = "OllamaHttpRequestError";
                return new MessageEnvelope(
                    payload: $"Error: Network communication with Ollama failed. {ex.Message}",
                    metadata: responseMetadata,
                    id: Guid.NewGuid().ToString(),
                    headers: responseHeaders);
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"LLMAgent ({Id}) task was canceled: {ex.Message}");
                responseHeaders[AgctorMessageHeaders.MessageType] = "TaskCanceledError";
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
                responseHeaders[AgctorMessageHeaders.MessageType] = "UnexpectedError";
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
                    { AgctorMessageHeaders.SenderId, ParentAgentId ?? "root" },
                    { AgctorMessageHeaders.ReceiverId, Id }
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