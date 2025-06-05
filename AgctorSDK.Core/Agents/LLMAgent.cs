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
        public string Model { get; set; }

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false; // We want a single response
    }

    public class OllamaGenerateResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("response")]
        public string Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
        
        // Other fields like context, durations, etc., are ignored for simplicity for now.
    }

    /// <summary>
    /// LLMAgent that communicates with a local Ollama instance.
    /// As per PRD: prd-core-001.md
    /// </summary>
    public class LLMAgent : IActor
    {
        public string Id { get; private set; }
        public string ActorType => "LLMAgent"; 
        public ActorState State { get; private set; } = ActorState.Initializing;
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged; // Changed event type

        private readonly HttpClient _httpClient;
        private readonly string _ollamaApiUrl;
        private readonly string _defaultModel;

        public LLMAgent(string id, string ollamaApiUrl = "http://localhost:11434", string defaultModel = "mistral")
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            _httpClient = new HttpClient();
            _ollamaApiUrl = ollamaApiUrl.EndsWith("/") ? ollamaApiUrl : ollamaApiUrl + "/";
            _defaultModel = defaultModel;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default) // Added CancellationToken
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

            SetState(ActorState.Active); 
            // await Task.CompletedTask; // Not needed for async method that awaits
        }

        public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken)
        {
            if (State != ActorState.Active)
            {
                Console.WriteLine($"LLMAgent ({Id}) received message while not active. State: {State}");
                return new MessageEnvelope($"Agent not active. Current state: {State}", envelope.Id, new DefaultMessageMetadata(Id, envelope.Id)); // Added placeholder metadata
            }

            if (envelope.Payload is not string prompt || string.IsNullOrWhiteSpace(prompt))
            {
                Console.WriteLine($"LLMAgent ({Id}) received invalid prompt payload.");
                return new MessageEnvelope("Error: Prompt must be a non-empty string.", envelope.Id, new DefaultMessageMetadata(Id, envelope.Id)); // Added placeholder metadata
            }
            
            try
            {
                var requestPayload = new OllamaGenerateRequest
                {
                    Model = _defaultModel, 
                    Prompt = prompt
                };

                HttpResponseMessage httpResponse = await _httpClient.PostAsJsonAsync(
                    _ollamaApiUrl + "api/generate", 
                    requestPayload, 
                    cancellationToken); // Ensure cancellationToken is used here

                if (httpResponse.IsSuccessStatusCode)
                {
                    var ollamaResponse = await httpResponse.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken);
                    if (ollamaResponse != null && ollamaResponse.Done)
                    {
                        return new MessageEnvelope(ollamaResponse.Response, envelope.Id, new DefaultMessageMetadata(Id, envelope.Id)); // Added placeholder metadata
                    }
                    else
                    {
                        string responseText = ollamaResponse?.Response ?? "no response text";
                        string errorDetail = ollamaResponse == null ? "null response object" : $"done flag is {ollamaResponse.Done}, response text: {responseText}";
                        Console.WriteLine($"LLMAgent ({Id}) received incomplete or non-final response from Ollama: {errorDetail}");
                        return new MessageEnvelope($"Error: Ollama did not return a final response. Detail: {errorDetail}", envelope.Id, new DefaultMessageMetadata(Id, envelope.Id)); // Added placeholder metadata
                    }
                }
                else
                {
                    string errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"LLMAgent ({Id}) error from Ollama API: {httpResponse.StatusCode}. Details: {errorContent}");
                    return new MessageEnvelope($"Error: Ollama API request failed with status {httpResponse.StatusCode}. Details: {errorContent}", envelope.Id, new DefaultMessageMetadata(Id, envelope.Id)); // Added placeholder metadata
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"LLMAgent ({Id}) HttpRequestException while communicating with Ollama: {ex.Message}");
                SetState(ActorState.Faulted); 
                return new MessageEnvelope($"Error: Network communication with Ollama failed. {ex.Message}", envelope.Id, new DefaultMessageMetadata(Id, envelope.Id)); // Added placeholder metadata
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"LLMAgent ({Id}) JsonException while processing Ollama response: {ex.Message}");
                return new MessageEnvelope($"Error: Failed to parse Ollama response. {ex.Message}", envelope.Id, new DefaultMessageMetadata(Id, envelope.Id)); // Added placeholder metadata
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"LLMAgent ({Id}) task was canceled: {ex.Message}");
                return new MessageEnvelope("Error: Task was canceled.", envelope.Id, new DefaultMessageMetadata(Id, envelope.Id)); // Added placeholder metadata
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LLMAgent ({Id}) an unexpected error occurred: {ex.Message}");
                SetState(ActorState.Faulted);
                return new MessageEnvelope($"Error: An unexpected error occurred. {ex.Message}", envelope.Id, new DefaultMessageMetadata(Id, envelope.Id)); // Added placeholder metadata
            }
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default) // Added CancellationToken
        {
            // Handle cancellation if any long-running shutdown operations are added
            if (cancellationToken.IsCancellationRequested) 
            {
                Console.WriteLine($"LLMAgent ({Id}) ShutdownAsync was canceled.");
                // Potentially throw OperationCanceledException or handle gracefully
                cancellationToken.ThrowIfCancellationRequested();
            }
            SetState(ActorState.Stopping);
            SetState(ActorState.Stopped);
            await Task.CompletedTask;
        }

        private void SetState(ActorState newState)
        {
            ActorState previousState = State;
            if (previousState == newState) return;
            State = newState;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previousState, newState));
        }
    }

    // The internal DefaultMessageMetadata class below will be removed.
    // internal class DefaultMessageMetadata : IMessageMetadata { ... }
} 