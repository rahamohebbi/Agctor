using AgctorSDK.Core.IntegrationTests.TestHelpers;
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

        protected override async Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken)
        {
            TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} processing prompt: {prompt}");

            try
            {
                var toolRequest = ParsePrompt(prompt);

                TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} parsed tool request: {toolRequest.Operation} with {toolRequest.Parameters.Count} parameters");
                foreach (var param in toolRequest.Parameters)
                {
                    TestDependencies.TestContext?.WriteLine($"  Parameter: {param.Key} = {param.Value}");
                }

                var result = await Handle(toolRequest).ConfigureAwait(false);
                TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} executed operation with result: Success={result.IsSuccess}, Output={result.Output}, Error={result.Error}");
                return result;
            }
            catch (Exception ex)
            {
                TestDependencies.TestContext?.WriteLine($"TestCodeExecutorTool {Id} encountered error: {ex.Message}");
                return new ToolResult { IsSuccess = false, Error = ex.Message };
            }
        }
    }
}
