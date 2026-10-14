using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Agents;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using AgctorSDK.Core.IntegrationTests.TestHelpers;

namespace AgctorSDK.Core.IntegrationTests
{
    [TestClass]
    [TestCategory("RequiresOllama")]
    public class LLMAgentIntegrationTests
    {
        private const string OllamaUrl = "http://localhost:11434";
        private const string TestModel = "mistral";

        private TestContext? _testContext;
        public TestContext TestContext 
        { 
            get => _testContext ?? throw new InvalidOperationException("TestContext not initialized");
            set => _testContext = value; 
        }

        [TestInitialize]
        public async Task TestInitialize()
        {
            TestContext.WriteLine("=== Integration Test Debug Session Started ===");
            TestContext.WriteLine($"Test: {TestContext.TestName}");
            TestContext.WriteLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            
            // Enhanced connectivity check
            var isConnected = await DebugHelper.VerifyOllamaConnectivity(OllamaUrl, TestModel, TestContext);
            if (!isConnected)
            {
                DebugHelper.PrintTroubleshootingGuide(TestContext);
                Assert.Inconclusive("Ollama connectivity check failed. Please ensure Ollama is running and the test model is available.");
            }
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Debug")]
        public async Task DebugReceiveAsync_WithDetailedLogging()
        {
            var stopwatch = Stopwatch.StartNew();
            TestContext.WriteLine("=== Starting Enhanced Debug Test ===");
            
            // Arrange
            var agentId = "debug-integration-test-llm-agent";
            TestContext.WriteLine($"Creating LLM Agent with ID: {agentId}");
            
            var agent = new LLMAgent(agentId, OllamaUrl, TestModel);
            DebugHelper.LogAgentState(agent, TestContext, "Initial");

            // Test initialization with detailed logging
            TestContext.WriteLine("Initializing agent...");
            var initTime = await DebugHelper.MeasureExecutionTime(async () => await agent.InitializeAsync());
            TestContext.WriteLine($"Initialization completed in {initTime.TotalMilliseconds:F2}ms");
            DebugHelper.LogAgentState(agent, TestContext, "After Init");

            // Create test message
            var prompt = "Explain what 2+2 equals in exactly one short sentence.";
            TestContext.WriteLine($"Test prompt: '{prompt}'");
            
            var headers = new Dictionary<string, string>
            {
                { "SenderId", "debug-integration-tester" },
                { "ReceiverId", agentId },
                { "MessageType", "UserPrompt" }
            };
            var metadata = new Dictionary<string, object>
            {
                { "CorrelationId", "debug-001" },
                { "Timestamp", DateTimeOffset.UtcNow }
            };
            var inputEnvelope = new MessageEnvelope(prompt, metadata, "debug-001", headers);
            DebugHelper.LogMessageEnvelope(inputEnvelope, TestContext, "Request");

            IMessageEnvelope? resultEnvelope = null;
            Exception? exception = null;
            TimeSpan responseTime = TimeSpan.Zero;

            // Act with detailed error tracking
            TestContext.WriteLine("Sending message to agent...");
            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)); // Longer timeout for debugging
                
                responseTime = await DebugHelper.MeasureExecutionTime(async () => 
                {
                    resultEnvelope = await agent.ReceiveAsync(inputEnvelope, cts.Token);
                });
                
                TestContext.WriteLine($"✅ Received response after {responseTime.TotalMilliseconds:F2}ms");
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                TestContext.WriteLine($"❌ Request timed out after {responseTime.TotalMilliseconds:F2}ms");
                TestContext.WriteLine($"Exception: {ex.Message}");
                exception = ex;
            }
            catch (HttpRequestException ex)
            {
                TestContext.WriteLine($"❌ HTTP error after {responseTime.TotalMilliseconds:F2}ms");
                TestContext.WriteLine($"Exception: {ex.Message}");
                TestContext.WriteLine("💡 This suggests Ollama connectivity issues");
                exception = ex;
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"❌ Unexpected error after {responseTime.TotalMilliseconds:F2}ms");
                TestContext.WriteLine($"Exception Type: {ex.GetType().Name}");
                TestContext.WriteLine($"Message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    TestContext.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                TestContext.WriteLine($"Stack Trace: {ex.StackTrace}");
                exception = ex;
            }

            stopwatch.Stop();
            TestContext.WriteLine($"Total test execution time: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");

            // Enhanced Assert with debugging info
            if (exception != null)
            {
                TestContext.WriteLine("=== Test Failed - Debugging Information ===");
                DebugHelper.LogAgentState(agent, TestContext, "Final");
                DebugHelper.PrintTroubleshootingGuide(TestContext);
                Assert.Fail($"Test failed with exception: {exception.Message}");
            }

            Assert.IsNotNull(resultEnvelope, "Result envelope should not be null.");
            DebugHelper.LogMessageEnvelope(resultEnvelope, TestContext, "Response");

            // Validate response structure
            TestContext.WriteLine("=== Response Validation ===");
            
            // Check correlation ID
            var responseCorrelationId = resultEnvelope.Metadata.TryGetValue("CorrelationId", out var corrId) ? corrId?.ToString() : null;
            TestContext.WriteLine($"Expected CorrelationId: debug-001, Actual: {responseCorrelationId}");
            Assert.AreEqual("debug-001", responseCorrelationId, "Response envelope CorrelationId should match request CorrelationId.");

            // Check payload type
            Assert.IsInstanceOfType(resultEnvelope.Payload, typeof(string), "Payload should be a string.");
            
            string? responsePayload = resultEnvelope.Payload as string;
            TestContext.WriteLine($"Response payload length: {responsePayload?.Length ?? 0} characters");
            
            // Check for valid response
            Assert.IsFalse(string.IsNullOrWhiteSpace(responsePayload), "Response payload from Ollama should not be null or empty.");
            Assert.IsFalse(responsePayload?.StartsWith("Error:"), $"Response payload should not be an error message from the agent. Actual: {responsePayload}");

            // Check response headers
            var messageType = resultEnvelope.Headers.TryGetValue("MessageType", out var msgType) ? msgType : "Unknown";
            TestContext.WriteLine($"Response MessageType: {messageType}");
            Assert.AreEqual("LLMResponse", messageType, "Response should be of type LLMResponse");

            TestContext.WriteLine($"✅ Test completed successfully!");
            TestContext.WriteLine($"Final response: {responsePayload}");
        }

        [TestMethod]
        [TestCategory("Integration")]
        [TestCategory("Debug")]
        public async Task DebugAgent_StateTransitions()
        {
            TestContext.WriteLine("=== Testing Agent State Transitions ===");
            
            var agentId = "debug-state-test-agent";
            var agent = new LLMAgent(agentId, OllamaUrl, TestModel);
            
            // Track state changes
            var stateChanges = new List<(ActorState Previous, ActorState New, DateTime When)>();
            agent.StateChanged += (sender, args) =>
            {
                stateChanges.Add((args.PreviousState, args.NewState, DateTime.UtcNow));
                TestContext.WriteLine($"State change: {args.PreviousState} -> {args.NewState}");
            };

            DebugHelper.LogAgentState(agent, TestContext, "Initial");
            
            // Test initialization
            await agent.InitializeAsync();
            DebugHelper.LogAgentState(agent, TestContext, "After Init");
            
            // Test shutdown
            await agent.ShutdownAsync();
            DebugHelper.LogAgentState(agent, TestContext, "After Shutdown");
            
            TestContext.WriteLine($"Total state changes observed: {stateChanges.Count}");
            foreach (var change in stateChanges)
            {
                TestContext.WriteLine($"  {change.When:HH:mm:ss.fff}: {change.Previous} -> {change.New}");
            }
        }
    }
}
