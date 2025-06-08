using AgctorSDK.Core.Tools.LanguageExecutors;
using System.Threading.Tasks;
using Xunit;

namespace AgctorSDK.Core.Tests
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
            Xunit.Assert.NotNull(csharpExecutor);
            Xunit.Assert.Equal("csharp", csharpExecutor.Language);
            
            var pythonExecutor = factory.GetExecutor("python");
            Xunit.Assert.NotNull(pythonExecutor);
            Xunit.Assert.Equal("python", pythonExecutor.Language);
        }
        
        [Fact]
        public void LanguageExecutorFactory_ShouldHandleCaseInsensitiveLanguages()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            
            // Act & Assert
            var csharpExecutor = factory.GetExecutor("CSharp");
            Xunit.Assert.NotNull(csharpExecutor);
            Xunit.Assert.Equal("csharp", csharpExecutor.Language);
            
            var pythonExecutor = factory.GetExecutor("PYTHON");
            Xunit.Assert.NotNull(pythonExecutor);
            Xunit.Assert.Equal("python", pythonExecutor.Language);
        }
        
        [Fact]
        public void LanguageExecutorFactory_ShouldHandleVariantNames()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            
            // Act & Assert
            var csharpExecutor = factory.GetExecutor("c#");
            Xunit.Assert.NotNull(csharpExecutor);
            
            var pythonExecutor = factory.GetExecutor("python3");
            Xunit.Assert.NotNull(pythonExecutor);
        }
        
        [Fact]
        public void LanguageExecutorFactory_ShouldReturnNullForUnknownLanguage()
        {
            // Arrange
            var factory = new LanguageExecutorFactory();
            
            // Act & Assert
            var rubyExecutor = factory.GetExecutor("ruby");
            Xunit.Assert.Null(rubyExecutor);
            
            var goExecutor = factory.GetExecutor("golang");
            Xunit.Assert.Null(goExecutor);
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
            Xunit.Assert.NotNull(rubyExecutor);
            Xunit.Assert.Equal("ruby", rubyExecutor.Language);
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
                return Task.FromResult((true, $"Executed {code} with {Language}", string.Empty));
            }
        }
    }
} 