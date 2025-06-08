using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.LanguageExecutors;
using AgctorSDK.Core.Tools.Models;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace CodeExecutorTests
{
    public class CodeExecutorToolTests
    {
        [Fact]
        public async Task RunCSharpCode_ShouldExecuteValidCode()
        {
            // Arrange
            var mockExecutorFactory = new Mock<ILanguageExecutorFactory>();
            var mockCSharpExecutor = new Mock<ILanguageExecutor>();
            mockCSharpExecutor.Setup(e => e.Language).Returns("csharp");
            mockCSharpExecutor.Setup(e => e.ExecuteCodeAsync(It.IsAny<string>()))
                .ReturnsAsync((true, "Hello from C#", string.Empty));

            mockExecutorFactory.Setup(f => f.GetExecutor("csharp")).Returns(mockCSharpExecutor.Object);

            var tool = new CodeExecutorTool("test-executor", null, mockExecutorFactory.Object);

            // Act
            var request = new ToolRequest
            {
                ToolName = "CodeExecutorTool",
                Operation = "RunCSharpCode",
                Parameters = new Dictionary<string, object>
                {
                    { "code", "Console.WriteLine(\"Hello from C#\");" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("Hello from C#", result.Output);
            Assert.Empty(result.Error);
            mockCSharpExecutor.Verify(e => e.ExecuteCodeAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RunPythonCode_ShouldExecuteValidCode()
        {
            // Arrange
            var mockExecutorFactory = new Mock<ILanguageExecutorFactory>();
            var mockPythonExecutor = new Mock<ILanguageExecutor>();
            mockPythonExecutor.Setup(e => e.Language).Returns("python");
            mockPythonExecutor.Setup(e => e.ExecuteCodeAsync(It.IsAny<string>()))
                .ReturnsAsync((true, "Hello from Python", string.Empty));

            mockExecutorFactory.Setup(f => f.GetExecutor("python")).Returns(mockPythonExecutor.Object);

            var tool = new CodeExecutorTool("test-executor", null, mockExecutorFactory.Object);

            // Act
            var request = new ToolRequest
            {
                ToolName = "CodeExecutorTool",
                Operation = "RunCode",
                Parameters = new Dictionary<string, object>
                {
                    { "code", "print('Hello from Python')" },
                    { "language", "python" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("Hello from Python", result.Output);
            Assert.Empty(result.Error);
            mockPythonExecutor.Verify(e => e.ExecuteCodeAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RunFile_ShouldUseCorrectLanguageBasedOnExtension()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.ReadAllTextAsync("test.py"))
                .ReturnsAsync("print('Hello from Python file')");

            var mockExecutorFactory = new Mock<ILanguageExecutorFactory>();
            var mockPythonExecutor = new Mock<ILanguageExecutor>();
            mockPythonExecutor.Setup(e => e.Language).Returns("python");
            mockPythonExecutor.Setup(e => e.ExecuteCodeAsync(It.IsAny<string>()))
                .ReturnsAsync((true, "Hello from Python file", string.Empty));

            mockExecutorFactory.Setup(f => f.GetExecutor("python")).Returns(mockPythonExecutor.Object);

            var tool = new CodeExecutorTool("test-executor", mockFileSystem.Object, mockExecutorFactory.Object);

            // Act
            var request = new ToolRequest
            {
                ToolName = "CodeExecutorTool",
                Operation = "RunFile",
                Parameters = new Dictionary<string, object>
                {
                    { "path", "test.py" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("Hello from Python file", result.Output);
            Assert.Empty(result.Error);
            mockPythonExecutor.Verify(e => e.ExecuteCodeAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RunFile_ShouldUseLanguageParameterOverExtension()
        {
            // Arrange
            var mockFileSystem = new Mock<IFileSystem>();
            mockFileSystem.Setup(fs => fs.ReadAllTextAsync("test.txt"))
                .ReturnsAsync("Console.WriteLine(\"Hello from C# in txt file\");");

            var mockExecutorFactory = new Mock<ILanguageExecutorFactory>();
            var mockCSharpExecutor = new Mock<ILanguageExecutor>();
            mockCSharpExecutor.Setup(e => e.Language).Returns("csharp");
            mockCSharpExecutor.Setup(e => e.ExecuteCodeAsync(It.IsAny<string>()))
                .ReturnsAsync((true, "Hello from C# in txt file", string.Empty));

            mockExecutorFactory.Setup(f => f.GetExecutor("csharp")).Returns(mockCSharpExecutor.Object);

            var tool = new CodeExecutorTool("test-executor", mockFileSystem.Object, mockExecutorFactory.Object);

            // Act
            var request = new ToolRequest
            {
                ToolName = "CodeExecutorTool",
                Operation = "RunFile",
                Parameters = new Dictionary<string, object>
                {
                    { "path", "test.txt" },
                    { "language", "csharp" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("Hello from C# in txt file", result.Output);
            Assert.Empty(result.Error);
            mockCSharpExecutor.Verify(e => e.ExecuteCodeAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RunCode_ShouldReturnError_WhenLanguageNotSupported()
        {
            // Arrange
            var mockExecutorFactory = new Mock<ILanguageExecutorFactory>();
            mockExecutorFactory.Setup(f => f.GetExecutor("ruby")).Returns((ILanguageExecutor)null);

            var tool = new CodeExecutorTool("test-executor", null, mockExecutorFactory.Object);

            // Act
            var request = new ToolRequest
            {
                ToolName = "CodeExecutorTool",
                Operation = "RunCode",
                Parameters = new Dictionary<string, object>
                {
                    { "code", "puts 'Hello from Ruby'" },
                    { "language", "ruby" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Unsupported language: ruby", result.Error);
        }
    }
}