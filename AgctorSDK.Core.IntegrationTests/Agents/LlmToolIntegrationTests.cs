using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.IntegrationTests.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.IntegrationTests.Agents
{
    // A simple agent to orchestrate the workflow.
    public class OrchestratorAgent : BaseActor
    {
        private readonly string _llmAgentId;
        private readonly string _codeEditorToolId;
        private readonly IActorRuntimeAdapter _runtime;

        public OrchestratorAgent(string id, string llmAgentId, string codeEditorToolId, IActorRuntimeAdapter runtime) : base(id, "OrchestratorAgent")
        {
            _llmAgentId = llmAgentId;
            _codeEditorToolId = codeEditorToolId;
            _runtime = runtime;
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is string prompt)
            {
                // 1. Send prompt to LLM
                var llmEnvelope = new MessageEnvelope(prompt, headers: new Dictionary<string, string> { { "ReceiverId", _llmAgentId } });
                var llmResponse = await _runtime.GetActorAsync<LLMAgent>(_llmAgentId);
                var llmResultEnvelope = await llmResponse.ReceiveAsync(llmEnvelope, cancellationToken);
                
                if (llmResultEnvelope.Payload is string code)
                {
                    // 2. Use CodeEditorTool to write the code to a file
                    var filePath = Path.GetTempFileName();
                    var toolRequest = new ToolRequest
                    {
                        Operation = "WriteFile",
                        Parameters = new Dictionary<string, object>
                        {
                            { "path", filePath },
                            { "content", code }
                        }
                    };

                    var toolEnvelope = new MessageEnvelope(toolRequest, headers: new Dictionary<string, string> { { "ReceiverId", _codeEditorToolId } });
                    var codeEditor = await _runtime.GetActorAsync<CodeEditorTool>(_codeEditorToolId);
                    await codeEditor.ReceiveAsync(toolEnvelope, cancellationToken);
                    
                    // Return the path to the created file.
                    return new MessageEnvelope(filePath);
                }
            }
            return new MessageEnvelope("Invalid prompt.");
        }
    }

    [TestClass]
    public class LlmToolIntegrationTests
    {
        private IActorRuntimeAdapter _runtime = null!;
        private IAgentFactory _agentFactory = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _runtime = new InMemoryActorRuntime();
            _runtime.InitializeAsync(new Dictionary<string, object>()).Wait();
            _agentFactory = new AgentFactory(_runtime);
        }

        [TestMethod]
        public async Task LlmAndCodeEditor_EndToEnd_Test()
        {
            // Arrange
            var llmAgentId = "llm-agent";
            var codeEditorToolId = "code-editor";
            var orchestratorId = "orchestrator";
            string? filePath = null;

            // Mock the LLM response
            var mockResponse = new OllamaGenerateResponse
            {
                Response = "public class HelloWorld { public static void Main() { System.Console.WriteLine(\"Hello, World!\"); } }",
                Done = true
            };
            var mockHttpHandler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonContent.Create(mockResponse));
            var httpClient = new HttpClient(mockHttpHandler);

            // Spawn actors
            await _runtime.SpawnActorAsync(llmAgentId, (id) => new LLMAgent(id, httpClient));
            await _runtime.SpawnActorAsync(codeEditorToolId, (id) => new CodeEditorTool(id));
            var orchestrator = await _runtime.SpawnActorAsync(orchestratorId, (id) => new OrchestratorAgent(id, llmAgentId, codeEditorToolId, _runtime));

            var prompt = "write hello world in C#";
            var envelope = new MessageEnvelope(prompt, headers: new Dictionary<string, string> { { "ReceiverId", orchestratorId } });
            
            try
            {
                // Act
                var resultEnvelope = await orchestrator.ReceiveAsync(envelope);
                filePath = resultEnvelope.Payload as string;

                // Assert
                Assert.IsNotNull(filePath);
                Assert.IsTrue(File.Exists(filePath));
                var fileContent = await File.ReadAllTextAsync(filePath);
                Assert.AreEqual(mockResponse.Response, fileContent);
            }
            finally
            {
                // Cleanup
                if (filePath != null && File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                await _runtime.StopActorAsync(llmAgentId);
                await _runtime.StopActorAsync(codeEditorToolId);
                await _runtime.StopActorAsync(orchestratorId);
            }
        }
    }
} 