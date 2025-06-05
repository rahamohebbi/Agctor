using Xunit;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces; // For IActor, ActorState, IMessageEnvelope
using AgctorSDK.Core.Messages;   // For MessageEnvelope
using AgctorSDK.Core.Agents;   // For LLMAgent, OllamaGenerateResponse

namespace AgctorSDK.Core.Tests // Corrected namespace
{
    /// <summary>
    /// Mock HttpMessageHandler to simulate HttpClient responses for testing.
    /// </summary>
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
        {
            _handlerFunc = handlerFunc;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handlerFunc(request, cancellationToken);
        }
    }

    public class LLMAgentTests : IDisposable // Added IDisposable for potential cleanup if constructor does setup
    {
        private readonly LLMAgent _agent;
        private readonly HttpClient _httpClient; // Made readonly
        private const string TestOllamaUrl = "http://localhost:11434/";
        private const string DefaultModel = "test-mistral";

        // Helper to create an HttpClient with a specific mock handler
        private HttpClient CreateMockHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
        {
            var localMockHandler = new MockHttpMessageHandler(handlerFunc); // Create and use locally
            return new HttpClient(localMockHandler) { BaseAddress = new Uri(TestOllamaUrl) };
        }
        
        private void InjectHttpClient(LLMAgent agent, HttpClient client)
        {
            var fieldInfo = typeof(LLMAgent).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fieldInfo != null)
            {
                fieldInfo.SetValue(agent, client);
            }
            else
            {
                throw new InvalidOperationException("_httpClient field not found in LLMAgent. This test setup might be outdated.");
            }
        }

        public LLMAgentTests()
        {
            _httpClient = CreateMockHttpClient(async (req, ct) => 
            {
                if (req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/tags"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{ \"models\": [] }") 
                    };
                }
                var defaultOllamaResponse = new OllamaGenerateResponse
                {
                    Model = DefaultModel,
                    CreatedAt = DateTime.UtcNow,
                    Response = "Default test response from Ollama",
                    Done = true
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(defaultOllamaResponse))
                };
            });
            _agent = new LLMAgent("test-agent-001", TestOllamaUrl, DefaultModel);
            InjectHttpClient(_agent, _httpClient);
        }

        [Fact]
        public async Task InitializeAsync_SetsStateToActive_OnSuccessfulHttpCheck()
        {
            await _agent.InitializeAsync();
            Assert.Equal(ActorState.Active, _agent.State);
        }

        [Fact]
        public async Task InitializeAsync_SetsStateToActive_EvenIfHttpCheckFails()
        {
            var localHttpClient = CreateMockHttpClient(async (req, ct) => 
            {
                 if (req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/tags"))
                 {
                     return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                 }
                 return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var localAgent = new LLMAgent("test-agent-init-fail", TestOllamaUrl, DefaultModel);
            InjectHttpClient(localAgent, localHttpClient);

            await localAgent.InitializeAsync();
            Assert.Equal(ActorState.Active, localAgent.State);
        }

        [Fact]
        public async Task ReceiveAsync_SuccessfulPrompt_ReturnsOllamaResponseInEnvelope()
        {
            await _agent.InitializeAsync(); 
            Assert.Equal(ActorState.Active, _agent.State);

            string prompt = "Explain quantum computing in simple terms.";
            string expectedResponseText = "Quantum computing is like... well, it's complicated!";
            var inputEnvelope = new MessageEnvelope(prompt, new DefaultMessageMetadata("test-sender", _agent.Id, "req-123"), "req-123");

            var localHttpClient = CreateMockHttpClient(async (req, ct) => 
            {
                if (req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/generate"))
                {
                    var ollamaResponse = new OllamaGenerateResponse
                    {
                        Model = DefaultModel,
                        CreatedAt = DateTime.UtcNow,
                        Response = expectedResponseText,
                        Done = true
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(ollamaResponse))
                    };
                }
                if (req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/tags"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"models\": [] }") };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            InjectHttpClient(_agent, localHttpClient);

            var resultEnvelope = await _agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Equal(inputEnvelope.Id, resultEnvelope.Id);
            Assert.IsType<string>(resultEnvelope.Payload);
            Assert.Equal(expectedResponseText, (string)resultEnvelope.Payload);
        }

        [Fact]
        public async Task ReceiveAsync_InvalidPrompt_ReturnsErrorEnvelope()
        {
            await _agent.InitializeAsync();
            var invalidPromptEnvelope = new MessageEnvelope(12345, new DefaultMessageMetadata("test-sender", _agent.Id, "req-invalid"), "req-invalid");

            var resultEnvelope = await _agent.ReceiveAsync(invalidPromptEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Equal(invalidPromptEnvelope.Id, resultEnvelope.Id);
            Assert.True(((string)resultEnvelope.Payload).StartsWith("Error: Prompt must be a non-empty string."), "Payload should indicate an invalid prompt error.");
        }
        
        [Fact]
        public async Task ReceiveAsync_EmptyStringPrompt_ReturnsErrorEnvelope()
        {
            await _agent.InitializeAsync();
            var emptyPromptEnvelope = new MessageEnvelope(string.Empty, new DefaultMessageMetadata("test-sender", _agent.Id, "req-empty"), "req-empty");

            var resultEnvelope = await _agent.ReceiveAsync(emptyPromptEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Equal(emptyPromptEnvelope.Id, resultEnvelope.Id);
            Assert.True(((string)resultEnvelope.Payload).StartsWith("Error: Prompt must be a non-empty string."), "Payload should indicate an invalid prompt error for empty string.");
        }

        [Fact]
        public async Task ReceiveAsync_OllamaApiReturnsError_ReturnsErrorEnvelopeWithDetails()
        {
            await _agent.InitializeAsync();
            string prompt = "A valid prompt.";
            var inputEnvelope = new MessageEnvelope(prompt, new DefaultMessageMetadata("test-sender", _agent.Id, "req-api-error"), "req-api-error");
            string apiErrorDetails = "{\"error\": \"model not found\"}";

            var localHttpClient = CreateMockHttpClient(async (req, ct) => 
            {
                 if (req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/generate"))
                 {
                     return new HttpResponseMessage(HttpStatusCode.NotFound) 
                     {
                         Content = new StringContent(apiErrorDetails) 
                     };
                 }
                 return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"models\": [] }") };
            });
            InjectHttpClient(_agent, localHttpClient);

            var resultEnvelope = await _agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Equal(inputEnvelope.Id, resultEnvelope.Id);
            Assert.Contains("Ollama API request failed with status NotFound", (string)resultEnvelope.Payload);
            Assert.Contains(apiErrorDetails, (string)resultEnvelope.Payload);
        }

        [Fact]
        public async Task ReceiveAsync_OllamaReturnsNonFinalResponse_ReturnsErrorEnvelope()
        {
            await _agent.InitializeAsync();
            string prompt = "Test prompt.";
            var inputEnvelope = new MessageEnvelope(prompt, new DefaultMessageMetadata("test-sender", _agent.Id, "req-non-final"), "req-non-final");

            var localHttpClient = CreateMockHttpClient(async (req, ct) => 
            {
                if (req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/generate"))
                {
                    var ollamaResponse = new OllamaGenerateResponse
                    {
                        Model = DefaultModel,
                        CreatedAt = DateTime.UtcNow,
                        Response = "Partial data...",
                        Done = false 
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(ollamaResponse))
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"models\": [] }") };
            });
            InjectHttpClient(_agent, localHttpClient);

            var resultEnvelope = await _agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Equal(inputEnvelope.Id, resultEnvelope.Id);
            Assert.Contains("Ollama did not return a final response", (string)resultEnvelope.Payload);
        }

        [Fact]
        public async Task ReceiveAsync_NetworkException_ReturnsErrorEnvelopeAndSetsStateToFaulted()
        {
            await _agent.InitializeAsync(); 
            string prompt = "Test prompt.";
            var inputEnvelope = new MessageEnvelope(prompt, new DefaultMessageMetadata("test-sender", _agent.Id, "req-network-error"), "req-network-error");

            var localHttpClient = CreateMockHttpClient(async (req, ct) => 
            {
                if (req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/generate"))
                {
                    throw new HttpRequestException("Simulated network failure");
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"models\": [] }") };
            });
           InjectHttpClient(_agent, localHttpClient);

            var resultEnvelope = await _agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Equal(inputEnvelope.Id, resultEnvelope.Id);
            Assert.Contains("Network communication with Ollama failed", (string)resultEnvelope.Payload);
            Assert.Equal(ActorState.Faulted, _agent.State); 
        }
        
        [Fact]
        public async Task ReceiveAsync_JsonExceptionDuringOllamaResponseParsing_ReturnsErrorEnvelope()
        {
            await _agent.InitializeAsync();
            string prompt = "Test prompt for JsonException";
            var inputEnvelope = new MessageEnvelope(prompt, new DefaultMessageMetadata("test-sender", _agent.Id, "req-json-error"), "req-json-error");
            string malformedJsonResponse = "{\"response\": \"This is not valid JSON because it\'s cut off";

            var localHttpClient = CreateMockHttpClient(async (req, ct) =>
            {
                if (req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/generate"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(malformedJsonResponse)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"models\": [] }") };
            });
            InjectHttpClient(_agent, localHttpClient);

            var resultEnvelope = await _agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Equal(inputEnvelope.Id, resultEnvelope.Id);
            Assert.Contains("Failed to parse Ollama response", (string)resultEnvelope.Payload);
        }

        [Fact]
        public async Task ReceiveAsync_WhenNotActive_ReturnsErrorEnvelope()
        {
            var freshAgent = new LLMAgent("test-agent-inactive", TestOllamaUrl, DefaultModel);
            Assert.Equal(ActorState.Initializing, freshAgent.State); 

            var inputEnvelope = new MessageEnvelope("A prompt to an inactive agent", new DefaultMessageMetadata("test-sender", freshAgent.Id, "req-inactive"), "req-inactive");
            var resultEnvelope = await freshAgent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Equal(inputEnvelope.Id, resultEnvelope.Id);
            Assert.True(((string)resultEnvelope.Payload).StartsWith("Agent not active."), "Payload should indicate agent is not active.");
        }

        [Fact]
        public async Task ReceiveAsync_TaskCanceled_ReturnsErrorEnvelope()
        {
            await _agent.InitializeAsync();
            var inputEnvelope = new MessageEnvelope("A prompt that will be cancelled", new DefaultMessageMetadata("test-sender", _agent.Id, "req-cancel"), "req-cancel");
            using var cts = new CancellationTokenSource();

            var localHttpClient = CreateMockHttpClient(async (req, ct_internal) => 
            {
                if (req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/generate"))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct_internal); 
                    ct_internal.ThrowIfCancellationRequested();
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Should have been cancelled") }; 
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"models\": [] }") };
            });
            InjectHttpClient(_agent, localHttpClient);
            
            cts.CancelAfter(100); 
            var resultEnvelope = await _agent.ReceiveAsync(inputEnvelope, cts.Token);

            Assert.NotNull(resultEnvelope);
            Assert.Equal(inputEnvelope.Id, resultEnvelope.Id);
            Assert.Contains("Task was canceled", (string)resultEnvelope.Payload);
        }

        [Fact]
        public async Task ShutdownAsync_SetsStateToStopped()
        {
            await _agent.InitializeAsync();
            Assert.Equal(ActorState.Active, _agent.State);

            await _agent.ShutdownAsync();
            Assert.Equal(ActorState.Stopped, _agent.State);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            GC.SuppressFinalize(this); // Recommended for IDisposable
        }
    }
} 