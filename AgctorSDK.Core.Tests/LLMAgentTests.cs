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

        // Helper to create a MCP-compliant input envelope for tests
        private MessageEnvelope CreateTestInputEnvelope(string agentId, string payload, string messageId, string senderId = "test-sender", string? correlationId = null, string messageType = "TestPrompt")
        {
            var metadata = new Dictionary<string, object>
            {
                { "Timestamp", DateTimeOffset.UtcNow }
            };
            if (correlationId != null) metadata["CorrelationId"] = correlationId;

            var headers = new Dictionary<string, string>
            {
                { "SenderId", senderId },
                { "ReceiverId", agentId },
                { "MessageType", messageType },
                { "Version", "1.0" }
            };
            return new MessageEnvelope(payload, metadata, messageId, headers);
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
        public async Task ReceiveAsync_SuccessfulPrompt_ReturnsOllamaResponseInEnvelope_MCP()
        {
            await _agent.InitializeAsync(); 
            Assert.Equal(ActorState.Active, _agent.State);

            string prompt = "Explain quantum computing in simple terms.";
            string expectedResponseText = "Quantum computing is like... well, it's complicated!";
            string inputMessageId = "req-mcp-123";
            string inputCorrelationId = "corr-mcp-123";
            var inputEnvelope = CreateTestInputEnvelope(_agent.Id, prompt, inputMessageId, correlationId: inputCorrelationId, messageType: "UserPrompt");

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
            Assert.NotEqual(inputEnvelope.Id, resultEnvelope.Id); // LLMAgent creates a new ID for the response
            Assert.IsType<string>(resultEnvelope.Payload);
            Assert.Equal(expectedResponseText, (string)resultEnvelope.Payload);
            Assert.Equal(_agent.Id, resultEnvelope.Headers["SenderId"]);
            Assert.Equal(inputEnvelope.Headers["SenderId"], resultEnvelope.Headers["ReceiverId"]);
            Assert.Equal("LLMResponse", resultEnvelope.Headers["MessageType"]);
            Assert.Equal(inputCorrelationId, resultEnvelope.Metadata["CorrelationId"]);
        }

        [Fact]
        public async Task ReceiveAsync_InvalidPrompt_ReturnsErrorEnvelope_MCP()
        {
            await _agent.InitializeAsync();
            var invalidPromptEnvelope = CreateTestInputEnvelope(_agent.Id, payload: 12345.ToString(), "req-invalid", correlationId: "corr-invalid");

            var resultEnvelope = await _agent.ReceiveAsync(invalidPromptEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.True(((string)resultEnvelope.Payload).StartsWith("Error: Prompt must be a non-empty string."), "Payload should indicate an invalid prompt error.");
            Assert.Equal(_agent.Id, resultEnvelope.Headers["SenderId"]);
            Assert.Equal(invalidPromptEnvelope.Headers["SenderId"], resultEnvelope.Headers["ReceiverId"]);
            Assert.Equal("InvalidPromptError", resultEnvelope.Headers["MessageType"]);
            Assert.Equal("corr-invalid", resultEnvelope.Metadata["CorrelationId"]);
        }
        
        [Fact]
        public async Task ReceiveAsync_EmptyStringPrompt_ReturnsErrorEnvelope_MCP()
        {
            await _agent.InitializeAsync();
            var emptyPromptEnvelope = CreateTestInputEnvelope(_agent.Id, string.Empty, "req-empty", correlationId: "corr-empty");

            var resultEnvelope = await _agent.ReceiveAsync(emptyPromptEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.True(((string)resultEnvelope.Payload).StartsWith("Error: Prompt must be a non-empty string."), "Payload should indicate an invalid prompt error for empty string.");
            Assert.Equal("InvalidPromptError", resultEnvelope.Headers["MessageType"]);
            Assert.Equal("corr-empty", resultEnvelope.Metadata["CorrelationId"]);
        }

        [Fact]
        public async Task ReceiveAsync_OllamaApiReturnsError_ReturnsErrorEnvelopeWithDetails_MCP()
        {
            await _agent.InitializeAsync();
            string prompt = "A valid prompt.";
            var inputEnvelope = CreateTestInputEnvelope(_agent.Id, prompt, "req-api-error", correlationId: "corr-api-error");
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
            Assert.Contains(apiErrorDetails, (string)resultEnvelope.Payload);
            Assert.Equal("OllamaApiError", resultEnvelope.Headers["MessageType"]);
            Assert.Equal("corr-api-error", resultEnvelope.Metadata["CorrelationId"]);
        }

        [Fact]
        public async Task ReceiveAsync_OllamaReturnsNonFinalResponse_ReturnsErrorEnvelope_MCP()
        {
            await _agent.InitializeAsync();
            var inputEnvelope = CreateTestInputEnvelope(_agent.Id, "prompt", "req-non-final", correlationId: "corr-non-final");
            
            var localHttpClient = CreateMockHttpClient(async (req, ct) => 
            {
                if (req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.PathAndQuery.EndsWith("/api/generate"))
                {
                    var ollamaResponse = new OllamaGenerateResponse { Done = false, Response = "partial..." }; // Non-final
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
            Assert.Contains("Error: Ollama did not return a final response", (string)resultEnvelope.Payload);
            Assert.Equal("OllamaIncompleteResponseError", resultEnvelope.Headers["MessageType"]);
            Assert.Equal("corr-non-final", resultEnvelope.Metadata["CorrelationId"]);
        }

        [Fact]
        public async Task ReceiveAsync_NetworkException_ReturnsErrorEnvelopeAndSetsStateToFaulted_MCP()
        {
            await _agent.InitializeAsync();
            var inputEnvelope = CreateTestInputEnvelope(_agent.Id, "prompt", "req-net-ex", correlationId: "corr-net-ex");

            var localHttpClient = CreateMockHttpClient((req, ct) => 
                Task.FromException<HttpResponseMessage>(new HttpRequestException("Network down")));
            InjectHttpClient(_agent, localHttpClient);

            var resultEnvelope = await _agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Contains("Error: Network communication with Ollama failed.", (string)resultEnvelope.Payload);
            Assert.Equal(ActorState.Faulted, _agent.State);
            Assert.Equal("OllamaHttpRequestError", resultEnvelope.Headers["MessageType"]);
            Assert.Equal("corr-net-ex", resultEnvelope.Metadata["CorrelationId"]);
        }

        [Fact]
        public async Task ReceiveAsync_JsonExceptionDuringOllamaResponseParsing_ReturnsErrorEnvelope_MCP()
        {
            await _agent.InitializeAsync();
            var inputEnvelope = CreateTestInputEnvelope(_agent.Id, "prompt", "req-json-ex", correlationId: "corr-json-ex");

            var localHttpClient = CreateMockHttpClient(async (req, ct) => 
            {
                if (req.Method == HttpMethod.Post)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("this is not json") };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"models\": [] }") };
            });
            InjectHttpClient(_agent, localHttpClient);

            var resultEnvelope = await _agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Contains("Error: Failed to parse Ollama response.", (string)resultEnvelope.Payload);
            Assert.Equal("OllamaJsonError", resultEnvelope.Headers["MessageType"]);
            Assert.Equal("corr-json-ex", resultEnvelope.Metadata["CorrelationId"]);
        }

        [Fact]
        public async Task ReceiveAsync_WhenNotActive_ReturnsErrorEnvelope_MCP()
        {
            // Don't initialize, or set to a non-active state manually if possible
            // For LLMAgent, InitializeAsync sets it to Active. We can test by not calling it or by setting state if a method existed.
            // The LLMAgent constructor sets state to Initializing. So, we can test with a fresh agent.
            var freshAgent = new LLMAgent("fresh-agent", TestOllamaUrl, DefaultModel);
            // Inject a default http client to avoid null ref if constructor doesn't fully init it for this path.
            InjectHttpClient(freshAgent, _httpClient); 
            Assert.Equal(ActorState.Initializing, freshAgent.State); // Agent starts as Initializing

            var inputEnvelope = CreateTestInputEnvelope(freshAgent.Id, "prompt", "req-not-active", correlationId: "corr-not-active");
            var resultEnvelope = await freshAgent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            Assert.NotNull(resultEnvelope);
            Assert.Contains("Agent not active", (string)resultEnvelope.Payload);
            Assert.Equal("AgentNotActiveError", resultEnvelope.Headers["MessageType"]);
            Assert.Equal("corr-not-active", resultEnvelope.Metadata["CorrelationId"]);
        }

        [Fact]
        public async Task ReceiveAsync_TaskCanceled_ReturnsErrorEnvelope_MCP()
        {
            await _agent.InitializeAsync();
            var inputEnvelope = CreateTestInputEnvelope(_agent.Id, "prompt", "req-canceled", correlationId: "corr-canceled");
            var cts = new CancellationTokenSource();

            var localHttpClient = CreateMockHttpClient(async (req, ctHttp) => 
            {
                if (req.Method == HttpMethod.Post)
                {
                    await Task.Delay(100, ctHttp); // Simulate work that can be cancelled
                    ctHttp.ThrowIfCancellationRequested(); // Will throw if cts.Cancel() was called
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError); // Should not be reached if cancelled
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"models\": [] }") };
            });
            InjectHttpClient(_agent, localHttpClient);

            var receiveTask = _agent.ReceiveAsync(inputEnvelope, cts.Token);
            cts.Cancel();

            var resultEnvelope = await receiveTask;

            Assert.NotNull(resultEnvelope);
            Assert.Contains("Error: Task was canceled.", (string)resultEnvelope.Payload);
            Assert.Equal("TaskCanceledError", resultEnvelope.Headers["MessageType"]);
            Assert.Equal("corr-canceled", resultEnvelope.Metadata["CorrelationId"]);
        }

        [Fact]
        public async Task ShutdownAsync_SetsStateToStopped()
        {
            await _agent.InitializeAsync();
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