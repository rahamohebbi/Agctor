using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.LanguageExecutors;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Tools.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AgctorSDK.Core.Tests
{
    public class CodeExecutorTests
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
                Operation = "RunCSharpCode",
                Parameters = new Dictionary<string, object>
                {
                    { "code", "Console.WriteLine(\"Hello from C#\");" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Xunit.Assert.True(result.IsSuccess);
            Xunit.Assert.Equal("Hello from C#", result.Output);
            Xunit.Assert.Empty(result.Error);
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
                Operation = "RunCode",
                Parameters = new Dictionary<string, object>
                {
                    { "code", "print('Hello from Python')" },
                    { "language", "python" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Xunit.Assert.True(result.IsSuccess);
            Xunit.Assert.Equal("Hello from Python", result.Output);
            Xunit.Assert.Empty(result.Error);
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
                Operation = "RunFile",
                Parameters = new Dictionary<string, object>
                {
                    { "path", "test.py" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Xunit.Assert.True(result.IsSuccess);
            Xunit.Assert.Equal("Hello from Python file", result.Output);
            Xunit.Assert.Empty(result.Error);
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
                Operation = "RunFile",
                Parameters = new Dictionary<string, object>
                {
                    { "path", "test.txt" },
                    { "language", "csharp" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Xunit.Assert.True(result.IsSuccess);
            Xunit.Assert.Equal("Hello from C# in txt file", result.Output);
            Xunit.Assert.Empty(result.Error);
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
                Operation = "RunCode",
                Parameters = new Dictionary<string, object>
                {
                    { "code", "puts 'Hello from Ruby'" },
                    { "language", "ruby" }
                }
            };

            var result = await tool.Handle(request);

            // Assert
            Xunit.Assert.False(result.IsSuccess);
            Xunit.Assert.Contains("Unsupported language: ruby", result.Error);
        }

        [Fact]
        public async Task ParsePrompt_ShouldParseLanguageParameter()
        {
            // Arrange
            var tool = new CodeExecutorTool("test-executor");
            
            // Act
            var prompt = "CodeExecutorTool RunCode --language python --code \"print('Hello')\"";
            var request = tool.ParsePrompt(prompt);
            
            // Assert
            Xunit.Assert.Equal("RunCode", request.Operation);
            Xunit.Assert.Equal("python", request.Parameters["language"]);
            Xunit.Assert.Equal("print('Hello')", request.Parameters["code"]);
        }

        [Fact]
        public void ParsePrompt_ShouldAcceptCaseInsensitiveToolPrefix()
        {
            var tool = new CodeExecutorTool("test-executor");
            var request = tool.ParsePrompt("codeexecutorTOOL RunCode --language python --code \"print(1)\"");
            Xunit.Assert.Equal("RunCode", request.Operation);
            Xunit.Assert.Equal("python", request.Parameters["language"]);
            Xunit.Assert.Equal("print(1)", request.Parameters["code"]);
        }

        [Fact]
        public void ParsePrompt_ShouldAcceptRunCodeLineWithoutToolPrefix()
        {
            var tool = new CodeExecutorTool("test-executor");
            var request = tool.ParsePrompt("RunCode --language python --code \"print(2)\"");
            Xunit.Assert.Equal("RunCode", request.Operation);
            Xunit.Assert.Equal("python", request.Parameters["language"]);
            Xunit.Assert.Equal("print(2)", request.Parameters["code"]);
        }

        [Fact]
        public void ParsePrompt_ShouldFindToolLineAfterProseAndNewlines()
        {
            var tool = new CodeExecutorTool("test-executor");
            var prompt = "Sure.\n\nCodeExecutorTool RunCode --language python --code \"print(3)\"";
            var request = tool.ParsePrompt(prompt);
            Xunit.Assert.Equal("RunCode", request.Operation);
            Xunit.Assert.Equal("print(3)", request.Parameters["code"]);
        }
    }
} 