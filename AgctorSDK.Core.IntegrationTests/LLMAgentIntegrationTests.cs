using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces; // For ActorState, IMessageEnvelope
using AgctorSDK.Core.Messages;   // For MessageEnvelope, DefaultMessageMetadata
using AgctorSDK.Core.Agents; // For LLMAgent
using System.Collections.Generic;

namespace AgctorSDK.IntegrationTests
{
    [TestClass]
    public class LLMAgentIntegrationTests
    {
        private const string OllamaUrl = "http://localhost:11434"; // Standard Ollama address
        private const string TestModel = "mistral"; // Assumes 'mistral' is pulled in local Ollama

        /// <summary>
        /// This test requires Ollama to be running locally with the 'mistral' model pulled.
        /// Run 'ollama pull mistral' if you haven't already.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")] // Optional: Categorize as an integration test
        public async Task ReceiveAsync_WithLiveOllama_ShouldReturnValidResponse()
        {
            // Arrange
            var agentId = "integration-test-llm-agent";
            var agent = new LLMAgent(agentId, OllamaUrl, TestModel);

            // Initialize the agent (this also performs a basic connectivity check in the current implementation)
            await agent.InitializeAsync();
            Assert.AreEqual(ActorState.Active, agent.State, "Agent should be active after initialization.");

            var prompt = "Why is the sky blue? Answer in one short sentence.";
            var headers = new Dictionary<string, string>
            {
                { "SenderId", "integration-tester" },
                { "ReceiverId", agentId },
                { "MessageType", "UserPrompt" }
            };
            var metadata = new Dictionary<string, object>
            {
                { "CorrelationId", "itest-001" },
                { "Timestamp", DateTimeOffset.UtcNow }
            };
            var inputEnvelope = new MessageEnvelope(prompt, metadata, "itest-001", headers);

            IMessageEnvelope? resultEnvelope = null;
            Exception? exception = null;

            // Act
            try
            {
                // Use a timeout for the integration test to prevent indefinite hanging
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); 
                resultEnvelope = await agent.ReceiveAsync(inputEnvelope, cts.Token);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            // Assert
            Assert.IsNull(exception, $"Ollama communication threw an exception: {exception?.Message}\nEnsure Ollama is running at {OllamaUrl} and model '{TestModel}' is available.");
            Assert.IsNotNull(resultEnvelope, "Result envelope should not be null.");
            
            Console.WriteLine($"LLM Agent ({agentId}) received payload: {resultEnvelope?.Payload}");

            Assert.AreEqual("itest-001", resultEnvelope?.Metadata["CorrelationId"]?.ToString(), "Response envelope CorrelationId should match request CorrelationId.");
            Assert.IsInstanceOfType(resultEnvelope?.Payload, typeof(string), "Payload should be a string.");
            
            string? responsePayload = resultEnvelope?.Payload as string;
            Assert.IsFalse(string.IsNullOrWhiteSpace(responsePayload), "Response payload from Ollama should not be null or empty.");
            Assert.IsFalse(responsePayload?.StartsWith("Error:"), $"Response payload should not be an error message from the agent. Actual: {responsePayload}");

            // Further assertions could check for specific content if the prompt was more deterministic,
            // but for a general LLM query, checking for non-empty and non-error is a good start.
            Console.WriteLine($"Successfully received response from Ollama model '{TestModel}': {responsePayload}");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task InitializeAsync_WithLiveOllama_ShouldAttemptConnectionAndBecomeActive()
        {
            // Arrange
            var agentId = "integration-init-test-agent";
            var agent = new LLMAgent(agentId, OllamaUrl, TestModel);
            Exception? exception = null;

            // Act
            try
            {
                await agent.InitializeAsync();
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            // Assert
            Assert.IsNull(exception, $"InitializeAsync threw an exception: {exception?.Message}\nEnsure Ollama is running at {OllamaUrl}.");
            // The current InitializeAsync logs a warning on failure but still sets state to Active.
            // A more robust test might involve checking logs or having InitializeAsync throw on critical failure.
            Assert.AreEqual(ActorState.Active, agent.State, "Agent should be active after initialization attempt.");
            Console.WriteLine($"LLM Agent ({agentId}) initialized. Current state: {agent.State}. Ensure Ollama is accessible at {OllamaUrl} for this test to be meaningful.");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task ReceiveAsync_InvalidPayload_ShouldReturnInvalidPromptError()
        {
            // Arrange
            var agentId = "itest-invalid-payload";
            var agent = new LLMAgent(agentId, OllamaUrl, TestModel);
            await agent.InitializeAsync();
            Assert.AreEqual(ActorState.Active, agent.State);

            var inputEnvelope = new MessageEnvelope(12345, null, "itest-002"); // Invalid payload type (not a string)

            // Act
            var resultEnvelope = await agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            // Assert
            Assert.IsNotNull(resultEnvelope);
            Assert.AreEqual("InvalidPromptError", resultEnvelope.Headers["MessageType"]);
            Assert.IsTrue(resultEnvelope.Payload.ToString().Contains("Prompt must be a non-empty string"));
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task ReceiveAsync_EmptyPrompt_ShouldReturnInvalidPromptError()
        {
            // Arrange
            var agentId = "itest-empty-prompt";
            var agent = new LLMAgent(agentId, OllamaUrl, TestModel);
            await agent.InitializeAsync();

            var inputEnvelope = new MessageEnvelope(" ", null, "itest-003"); // Empty prompt

            // Act
            var resultEnvelope = await agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            // Assert
            Assert.IsNotNull(resultEnvelope);
            Assert.AreEqual("InvalidPromptError", resultEnvelope.Headers["MessageType"]);
            Assert.IsTrue(resultEnvelope.Payload.ToString().Contains("Prompt must be a non-empty string"));
        }
        
        [TestMethod]
        [TestCategory("Integration")]
        public async Task ReceiveAsync_AgentNotActive_ShouldReturnNotActiveError()
        {
            // Arrange
            var agentId = "itest-not-active";
            var agent = new LLMAgent(agentId, OllamaUrl, TestModel);
            // Don't initialize, or shut it down to make it not active.
            await agent.ShutdownAsync(); 
            Assert.AreNotEqual(ActorState.Active, agent.State);

            var inputEnvelope = new MessageEnvelope("test prompt", null, "itest-004");

            // Act
            var resultEnvelope = await agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            // Assert
            Assert.IsNotNull(resultEnvelope);
            Assert.AreEqual("AgentNotActiveError", resultEnvelope.Headers["MessageType"]);
            Assert.IsTrue(resultEnvelope.Payload.ToString().Contains("Agent not active"));
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task ReceiveAsync_MissingSenderIdHeader_ShouldHandleGracefully()
        {
            // Arrange
            var agentId = "itest-missing-header";
            var agent = new LLMAgent(agentId, OllamaUrl, TestModel);
            await agent.InitializeAsync();

            var headers = new Dictionary<string, string> { { "ReceiverId", agentId } }; // No SenderId
            var inputEnvelope = new MessageEnvelope("Why is the sky blue?", null, "itest-005", headers);

            // Act
            var resultEnvelope = await agent.ReceiveAsync(inputEnvelope, CancellationToken.None);

            // Assert
            Assert.IsNotNull(resultEnvelope);
            // Check that the response was sent back to "unknown"
            Assert.AreEqual("unknown", resultEnvelope.Headers["ReceiverId"]);
            // Check it is a valid LLM response otherwise
            Assert.AreEqual("LLMResponse", resultEnvelope.Headers["MessageType"]);
            Assert.IsFalse(string.IsNullOrWhiteSpace(resultEnvelope.Payload as string));
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task ReceiveAsync_WithCancellation_ShouldReturnTaskCanceledError()
        {
            // Arrange
            var agentId = "itest-cancellation";
            var agent = new LLMAgent(agentId, OllamaUrl, TestModel);
            await agent.InitializeAsync();
            var cts = new CancellationTokenSource();
            
            var inputEnvelope = new MessageEnvelope("A very long prompt that takes time.", null, "itest-006");
            IMessageEnvelope? resultEnvelope = null;
            Exception? exception = null;

            // Act
            try
            {
                cts.Cancel(); // Cancel before the call
                resultEnvelope = await agent.ReceiveAsync(inputEnvelope, cts.Token);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            // Assert
            Assert.IsNull(exception, "ReceiveAsync should handle cancellation gracefully and not throw an exception.");
            Assert.IsNotNull(resultEnvelope, "Result envelope should not be null.");
            Assert.AreEqual("TaskCanceledError", resultEnvelope?.Headers["MessageType"]);
            Assert.IsTrue(resultEnvelope?.Payload.ToString().Contains("Task was canceled"));
        }
    }
} 