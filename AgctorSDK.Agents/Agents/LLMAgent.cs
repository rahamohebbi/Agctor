using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces; // For IActor, ActorState, IMessageEnvelope, IMessageMetadata, ActorStateChangedEventArgs
using AgctorSDK.Core.Messages;   // For MessageEnvelope
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
        public bool Stream { get; set; } = false; // We want a single response
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
        private readonly HttpClient _httpClient;
        private readonly string _ollamaApiUrl;
        private readonly string _defaultModel;

        public LLMAgent(string id) : this(id, new HttpClient())
        {
        }

        public LLMAgent(string id, HttpClient httpClient, string ollamaApiUrl = "http://localhost:11434", string defaultModel = "mistral")
            : base(id)
        {
            _httpClient = httpClient;
            _ollamaApiUrl = ollamaApiUrl.EndsWith("/") ? ollamaApiUrl : ollamaApiUrl + "/";
            _defaultModel = defaultModel;
        }

        public LLMAgent(string id, string ollamaApiUrl = "http://localhost:11434", string defaultModel = "mistral")
            : this(id, new HttpClient(), ollamaApiUrl, defaultModel)
        {
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
                    Prompt = prompt
                };

                HttpResponseMessage httpResponse = await _httpClient.PostAsJsonAsync(
                    _ollamaApiUrl + "api/generate",
                    requestPayloadOllama,
                    cancellationToken);

                if (httpResponse.IsSuccessStatusCode)
                {
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