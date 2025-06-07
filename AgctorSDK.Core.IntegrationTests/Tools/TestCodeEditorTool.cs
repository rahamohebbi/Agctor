using AgctorSDK.Core.IntegrationTests.TestHelpers;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.IntegrationTests.Tools
{
    public class TestCodeEditorTool : CodeEditorTool
    {
        private readonly IFileSystem _mockFileSystem;

        public TestCodeEditorTool(string id) : base(id, TestDependencies.MockFileSystem?.Object)
        {
            if (TestDependencies.MockFileSystem == null)
            {
                throw new InvalidOperationException("MockFileSystem has not been initialized.");
            }
            
            _mockFileSystem = TestDependencies.MockFileSystem.Object;
            TestDependencies.TestContext?.WriteLine($"Created TestCodeEditorTool with ID {id} and MockFileSystem {TestDependencies.MockFileSystem.GetHashCode()}");
        }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} processing prompt: {prompt}");
            
            try
            {
                // Parse the prompt into a tool request
                var toolRequest = ParsePromptToToolRequest(prompt);
                
                TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} parsed tool request: {toolRequest.Operation} with {toolRequest.Parameters.Count} parameters");
                foreach (var param in toolRequest.Parameters)
                {
                    TestDependencies.TestContext?.WriteLine($"  Parameter: {param.Key} = {param.Value}");
                }
                
                // Execute the tool operation directly using our overridden Handle method
                var result = await HandleTestRequest(toolRequest, cancellationToken);
                TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} executed operation with result: Success={result.IsSuccess}, Error={result.Error}");

                // Important: Send the completion message back to the parent agent
                if (ParentAgentId != null && AgentFactory?.RuntimeAdapter != null)
                {
                    TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} sending completion message to parent {ParentAgentId}");
                    // Create and send completion message
                    var completionMessage = new SubtaskCompletedMessage(Id, ParentAgentId, result);
                    var envelope = new MessageEnvelope(completionMessage);
                    await AgentFactory.RuntimeAdapter.SendMessageAsync(ParentAgentId, envelope, cancellationToken: cancellationToken);
                    TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} sent completion message successfully");
                }
                else
                {
                    TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} could not send completion message: ParentAgentId={ParentAgentId}, AgentFactory={AgentFactory != null}, RuntimeAdapter={AgentFactory?.RuntimeAdapter != null}");
                }
            }
            catch (Exception ex)
            {
                TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} encountered error: {ex.Message}");
                
                // Send failure message if possible
                if (ParentAgentId != null && AgentFactory?.RuntimeAdapter != null)
                {
                    var failureMessage = new SubtaskFailedMessage(Id, ParentAgentId, ex);
                    var envelope = new MessageEnvelope(failureMessage);
                    await AgentFactory.RuntimeAdapter.SendMessageAsync(ParentAgentId, envelope, cancellationToken: cancellationToken);
                    TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} sent failure message to parent");
                }
            }
        }
        
        // Test-specific handler to ensure we're using the mock file system
        private async Task<ToolResult> HandleTestRequest(ToolRequest request, CancellationToken cancellationToken)
        {
            TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} handling request: {request.Operation}");
            
            // For WriteFile operations, ensure we're using the mockFileSystem
            if (request.Operation == "WriteFile" && 
                request.Parameters.TryGetValue("path", out var pathObj) && 
                request.Parameters.TryGetValue("content", out var contentObj))
            {
                string path = pathObj as string;
                string content = contentObj as string;
                
                if (!string.IsNullOrEmpty(path) && content != null)
                {
                    TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} writing to file: {path} (content length: {content.Length})");
                    await _mockFileSystem.WriteAllTextAsync(path, content);
                    return new ToolResult { IsSuccess = true, Output = $"File written to {path}" };
                }
            }
            
            // Fall back to base implementation for other operations
            return await base.Handle(request);
        }
    }
} 