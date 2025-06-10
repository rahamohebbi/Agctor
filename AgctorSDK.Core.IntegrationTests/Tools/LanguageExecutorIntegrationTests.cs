using AgctorSDK.Core.Tools.LanguageExecutors;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace AgctorSDK.Core.IntegrationTests.Tools
{
    public class LanguageExecutorIntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public LanguageExecutorIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task CSharpExecutor_ShouldExecuteValidCode()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            var code = @"
using System;
class Program 
{
    static void Main() 
    {
        Console.WriteLine(""Hello from C# integration test!"");
        Console.WriteLine(""Success!"");
    }
}";

            // Act
            var executor = factory.GetExecutor("csharp");
            Assert.NotNull(executor);
            
            _output.WriteLine($"Executing code with {executor.Language} executor");
            var result = await executor.ExecuteCodeAsync(code);
            
            // Output for debugging
            _output.WriteLine($"Success: {result.Success}");
            if (!result.Success)
            {
                _output.WriteLine($"Error: {result.Error}");
            }
            _output.WriteLine("Output:");
            _output.WriteLine(result.Output);
            
            // Assert - just check that execution was successful, don't check specific output
            Assert.True(result.Success);
            Assert.DoesNotContain("error", result.Error.ToLower());
        }

        [Fact]
        public async Task PythonExecutor_ShouldExecuteValidCode()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            var code = @"
print('Hello from Python!')
for i in range(1, 6):
    print(f'Python Counter: {i}')
";

            // Act
            var executor = factory.GetExecutor("python");
            Assert.NotNull(executor);
            
            _output.WriteLine($"Executing code with {executor.Language} executor");
            var result = await executor.ExecuteCodeAsync(code);
            
            // Output for debugging
            _output.WriteLine($"Success: {result.Success}");
            if (!result.Success)
            {
                _output.WriteLine($"Error: {result.Error}");
            }
            _output.WriteLine("Output:");
            _output.WriteLine(result.Output);
            
            // Assert - just check that execution was successful, don't check specific output
            Assert.True(result.Success);
            Assert.DoesNotContain("error", result.Error.ToLower());
        }

        [Fact]
        public void LanguageAliases_ShouldResolveCorrectly()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            
            // Act & Assert
            var csharpExecutor = factory.GetExecutor("c#");
            Assert.NotNull(csharpExecutor);
            Assert.Equal("csharp", csharpExecutor.Language);
            
            var pythonExecutor = factory.GetExecutor("py");
            Assert.NotNull(pythonExecutor);
            Assert.Equal("python", pythonExecutor.Language);
            
            var rubyExecutor = factory.GetExecutor("ruby");
            Assert.Null(rubyExecutor);
        }

        [Fact]
        public async Task CustomExecutor_ShouldRegisterAndRun()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            var mockExecutor = new MockLanguageExecutor("ruby");
            
            // Act
            factory.RegisterExecutor(mockExecutor);
            
            // Assert - Registration
            var executor = factory.GetExecutor("ruby");
            Assert.NotNull(executor);
            Assert.Equal("ruby", executor.Language);
            
            // Test execution
            var result = await executor.ExecuteCodeAsync("puts 'Hello from Ruby!'");
            Assert.True(result.Success);
            Assert.Contains("Mock output for ruby", result.Output);
            Assert.Contains("puts 'Hello from Ruby!'", result.Output);
            Assert.Empty(result.Error);
        }
        
        // A simple mock executor for testing
        private class MockLanguageExecutor : ILanguageExecutor
        {
            public string Language { get; }
            
            public MockLanguageExecutor(string language)
            {
                Language = language;
            }
            
            public Task<(bool Success, string Output, string Error)> ExecuteCodeAsync(string code)
            {
                return Task.FromResult((true, $"Mock output for {Language}: {code}", string.Empty));
            }
        }
    }
} 