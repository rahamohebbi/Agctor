using AgctorSDK.Core.IntegrationTests.TestHelpers;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.IntegrationTests.Tools
{
    public class TestCodeExecutorTool : CodeExecutorTool
    {
        public TestCodeExecutorTool(string id) : base(id, TestDependencies.MockFileSystem?.Object)
        {
            if (TestDependencies.MockFileSystem == null)
            {
                throw new InvalidOperationException("MockFileSystem has not been initialized.");
            }
            
            TestDependencies.TestContext?.WriteLine($"Created TestCodeExecutorTool with ID {id}");
        }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} processing prompt: {prompt}");
            
            try
            {
                // Parse the prompt into a tool request
                var toolRequest = ParsePrompt(prompt);
                
                TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} parsed tool request: {toolRequest.Operation} with {toolRequest.Parameters.Count} parameters");
                foreach (var param in toolRequest.Parameters)
                {
                    TestDependencies.TestContext?.WriteLine($"  Parameter: {param.Key} = {param.Value}");
                }
                
                // Execute the tool operation
                var result = await Handle(toolRequest);
                TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} executed operation with result: Success={result.IsSuccess}, Output={result.Output}, Error={result.Error}");

                // Important: Send the completion message back to the parent agent
                if (ParentAgentId != null && AgentFactory?.RuntimeAdapter != null)
                {
                    TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} sending completion message to parent {ParentAgentId}");
                    // Create and send completion message
                    var completionMessage = new SubtaskCompletedMessage(Id, ParentAgentId, result);
                    var envelope = new MessageEnvelope(completionMessage);
                    await AgentFactory.RuntimeAdapter.SendMessageAsync(ParentAgentId, envelope, cancellationToken: cancellationToken);
                    TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} sent completion message successfully");
                }
                else
                {
                    TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} could not send completion message: ParentAgentId={ParentAgentId}, AgentFactory={AgentFactory != null}, RuntimeAdapter={AgentFactory?.RuntimeAdapter != null}");
                }
            }
            catch (Exception ex)
            {
                TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} encountered error: {ex.Message}");
                
                // Send failure message if possible
                if (ParentAgentId != null && AgentFactory?.RuntimeAdapter != null)
                {
                    var failureMessage = new SubtaskFailedMessage(Id, ParentAgentId, ex);
                    var envelope = new MessageEnvelope(failureMessage);
                    await AgentFactory.RuntimeAdapter.SendMessageAsync(ParentAgentId, envelope, cancellationToken: cancellationToken);
                    TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} sent failure message to parent");
                }
            }
        }
    }
} 