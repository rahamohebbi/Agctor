using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace AgctorSDK.Core.IntegrationTests.Tools
{
    public class CodeExecutorToolIntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public CodeExecutorToolIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ExecuteCSharpCode_ShouldSucceed()
        {
            // Arrange
            var tool = new CodeExecutorTool("integration-test-executor");
            var request = new ToolRequest
            {
                Operation = "RunCSharpCode",
                Parameters = new Dictionary<string, object>
                {
                    { "code", @"
using System;
class Program 
{
    static void Main() 
    {
        Console.WriteLine(""Hello from C# integration test!"");
        Console.WriteLine(""Success!"");
    }
}" }
                }
            };

            // Act
            _output.WriteLine("Executing C# code via CodeExecutorTool");
            var result = await tool.Handle(request);
            
            // Output for debugging
            _output.WriteLine($"Success: {result.IsSuccess}");
            _output.WriteLine($"Output: {result.Output}");
            _output.WriteLine($"Error: {result.Error}");
            
            // Assert - just check that execution was successful
            Assert.True(result.IsSuccess);
            Assert.DoesNotContain("error", result.Error.ToString().ToLower());
        }

        [Fact]
        public async Task ExecutePythonCode_ShouldSucceed()
        {
            // Arrange
            var tool = new CodeExecutorTool("integration-test-executor");
            var request = new ToolRequest
            {
                Operation = "RunCode",
                Parameters = new Dictionary<string, object>
                {
                    { "language", "python" },
                    { "code", @"
print('Hello from Python integration test!')
print('Success!')
" }
                }
            };

            // Act
            _output.WriteLine("Executing Python code via CodeExecutorTool");
            var result = await tool.Handle(request);
            
            // Output for debugging
            _output.WriteLine($"Success: {result.IsSuccess}");
            _output.WriteLine($"Output: {result.Output}");
            _output.WriteLine($"Error: {result.Error}");
            
            // Assert - just check that execution was successful
            Assert.True(result.IsSuccess);
            Assert.DoesNotContain("error", result.Error.ToString().ToLower());
        }

        [Fact]
        public async Task ParseAndExecutePrompt_ShouldSucceed()
        {
            // Arrange
            var tool = new CodeExecutorTool("integration-test-executor");
            var prompt = "CodeExecutorTool RunCode --language python --code \"print('Hello from prompt!')\"";
            
            // Act
            _output.WriteLine("Parsing and executing prompt");
            var request = tool.ParsePrompt(prompt);
            var result = await tool.Handle(request);
            
            // Output for debugging
            _output.WriteLine($"Success: {result.IsSuccess}");
            _output.WriteLine($"Output: {result.Output}");
            _output.WriteLine($"Error: {result.Error}");
            
            // Assert - just check that execution was successful
            Assert.True(result.IsSuccess);
            Assert.DoesNotContain("error", result.Error.ToString().ToLower());
        }
    }
} 