using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;

namespace AgctorSDK.Core.Tests
{
    [TestClass]
    public class LLMAgentTests
    {
        private Mock<HttpMessageHandler> _httpMessageHandlerMock;
        private HttpClient _httpClient;
        private LLMAgent _agent;

        private const string DefaultBaseUrl = "http://localhost:11434";
        private const string DefaultModel = "test-model";

        [TestInitialize]
        public void TestInitialize()
        {
            _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
            
            // Set up default model list response for initialization
            var modelListResponse = @"
            {
                ""models"": [
                    {
                        ""name"": ""test-model"",
                        ""modified_at"": ""2023-12-10T03:22:32Z"",
                        ""size"": 3791730107,
                        ""digest"": ""1f7a10c6d300bdd1b562ef3a904179a5"",
                        ""details"": {
                            ""format"": ""gguf"",
                            ""family"": ""llama"",
                            ""families"": [""llama""],
                            ""parameter_size"": ""7B"",
                            ""quantization_level"": ""Q4_0""
                        }
                    }
                ]
            }";
            
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.AbsolutePath.Contains("/api/tags")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(modelListResponse)
                });
            
            _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
            _agent = new LLMAgent("test-llm-agent", _httpClient, DefaultBaseUrl, DefaultModel);
        }

        private void SetupHttpMock_Success(string expectedResponse)
        {
            var ollamaResponse = new OllamaGenerateResponse
            {
                Response = expectedResponse,
                Done = true
            };
            
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.AbsolutePath.Contains("/api/generate")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(ollamaResponse))
                });
        }
        
        private void SetupHttpMock_Error(HttpStatusCode statusCode)
        {
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.AbsolutePath.Contains("/api/generate")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent("An error occurred")
                });
        }

        [TestMethod]
        public async Task InitializeAsync_ShouldSetStateToActive()
        {
            // Arrange - HTTP mock is set up in TestInitialize

            // Act
            await _agent.InitializeAsync();

            // Assert
            Assert.AreEqual(ActorState.Active, _agent.State);
        }
        
        [TestMethod]
        public async Task ReceiveAsync_SuccessfulPrompt_ReturnsResponse()
        {
            // Arrange
            SetupHttpMock_Success("Paris");
            await _agent.InitializeAsync();
            var prompt = "What is the capital of France?";
            var envelope = new MessageEnvelope(prompt);

            // Act
            var result = await _agent.ReceiveAsync(envelope, CancellationToken.None);

            // Assert
            Assert.AreEqual("Paris", result.Payload);
        }
        
        [TestMethod]
        public async Task ReceiveAsync_ApiError_ReturnsErrorEnvelope()
        {
            // Arrange
            SetupHttpMock_Error(HttpStatusCode.InternalServerError);
            await _agent.InitializeAsync();
            var prompt = "A prompt that will fail.";
            var envelope = new MessageEnvelope(prompt);

            // Act
            var result = await _agent.ReceiveAsync(envelope, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.Payload.ToString().Contains("Error: Ollama API request failed"));
        }
        
        [TestMethod]
        public async Task ReceiveAsync_InvalidPrompt_ReturnsErrorEnvelope()
        {
            // Arrange
            await _agent.InitializeAsync();
            var prompt = 123; // Invalid prompt type
            var envelope = new MessageEnvelope(prompt);

            // Act
            var result = await _agent.ReceiveAsync(envelope, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.Payload.ToString().Contains("Error: Prompt must be a non-empty string."));
        }
    }
} 