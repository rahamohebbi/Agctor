using AgctorSDK.Core.Tools.LanguageExecutors;
using System.Threading.Tasks;
using Xunit;

namespace CodeExecutorTests
{
    public class LanguageExecutorTests
    {
        [Fact]
        public void LanguageExecutorFactory_ShouldRegisterAndRetrieveExecutors()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            
            // Act & Assert
            var csharpExecutor = factory.GetExecutor("csharp");
            Assert.NotNull(csharpExecutor);
            Assert.Equal("csharp", csharpExecutor.Language);
            
            var pythonExecutor = factory.GetExecutor("python");
            Assert.NotNull(pythonExecutor);
            Assert.Equal("python", pythonExecutor.Language);
        }
        
        [Fact]
        public void LanguageExecutorFactory_ShouldHandleCaseInsensitiveLanguages()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            
            // Act & Assert
            var csharpExecutor = factory.GetExecutor("CSharp");
            Assert.NotNull(csharpExecutor);
            Assert.Equal("csharp", csharpExecutor.Language);
            
            var pythonExecutor = factory.GetExecutor("PYTHON");
            Assert.NotNull(pythonExecutor);
            Assert.Equal("python", pythonExecutor.Language);
        }
        
        [Fact]
        public void LanguageExecutorFactory_ShouldHandleLanguageAliases()
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
            
            var python3Executor = factory.GetExecutor("python3");
            Assert.NotNull(python3Executor);
            Assert.Equal("python", python3Executor.Language);
        }
        
        [Fact]
        public void LanguageExecutorFactory_ShouldReturnNullForUnknownLanguage()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            
            // Act & Assert
            var rubyExecutor = factory.GetExecutor("ruby");
            Assert.Null(rubyExecutor);
            
            var goExecutor = factory.GetExecutor("golang");
            Assert.Null(goExecutor);
        }
        
        [Fact]
        public void LanguageExecutorFactory_ShouldAllowCustomExecutorRegistration()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            var mockExecutor = new MockLanguageExecutor("ruby");
            
            // Act
            factory.RegisterExecutor(mockExecutor);
            
            // Assert
            var rubyExecutor = factory.GetExecutor("ruby");
            Assert.NotNull(rubyExecutor);
            Assert.Equal("ruby", rubyExecutor.Language);
            
            // Test that aliases work for custom executors too
            factory.RegisterLanguageAlias("rb", "ruby");
            var rbExecutor = factory.GetExecutor("rb");
            Assert.NotNull(rbExecutor);
            Assert.Equal("ruby", rbExecutor.Language);
        }
        
        // A simple mock executor for testing the factory
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