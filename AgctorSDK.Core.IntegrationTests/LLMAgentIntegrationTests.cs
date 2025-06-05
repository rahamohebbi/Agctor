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
    }
} 