using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AgctorSDK.Core.Tests.Tools
{
    [TestClass]
    public class FileSystemToolTests
    {
        private Mock<IFileSystem> _mockFileSystem;
        private FileSystemTool _fileSystemTool;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockFileSystem = new Mock<IFileSystem>();
            _fileSystemTool = new FileSystemTool("test-fs-tool", _mockFileSystem.Object);
        }

        [TestMethod]
        public async Task Handle_ReadFile_Success()
        {
            // Arrange
            var path = "test.txt";
            var content = "hello world";
            _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(path)).ReturnsAsync(content);
            var request = new ToolRequest
            {
                Operation = "ReadFile",
                Parameters = new Dictionary<string, object> { { "path", path } }
            };

            // Act
            var result = await _fileSystemTool.Handle(request);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(content, result.Output);
            Assert.IsTrue(string.IsNullOrEmpty(result.Error));
        }

        [TestMethod]
        public async Task Handle_WriteFile_Success()
        {
            // Arrange
            var path = "test.txt";
            var content = "hello world";
            _mockFileSystem.Setup(fs => fs.WriteAllTextAsync(path, content)).Returns(Task.CompletedTask);
            var request = new ToolRequest
            {
                Operation = "WriteFile",
                Parameters = new Dictionary<string, object> { { "path", path }, { "content", content } }
            };

            // Act
            var result = await _fileSystemTool.Handle(request);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            _mockFileSystem.Verify(fs => fs.WriteAllTextAsync(path, content), Times.Once);
        }

        [TestMethod]
        public async Task Handle_ListDirectory_ReturnsEntriesJson()
        {
            var dir = Path.Combine(Path.GetTempPath(), "fs-tool-list-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(dir, "listed.txt"), "x");
                var tool = new FileSystemTool("fs-list", fileSystem: null);
                var request = new ToolRequest
                {
                    Operation = "ListDirectory",
                    Parameters = new Dictionary<string, object> { { "path", dir } }
                };

                var result = await tool.Handle(request);

                Assert.IsTrue(result.IsSuccess);
                var s = result.Output?.ToString() ?? string.Empty;
                StringAssert.Contains(s, "listed.txt");
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [TestMethod]
        public async Task Handle_DeletePath_DeletesFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "fs-tool-del-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "gone.txt");
            await File.WriteAllTextAsync(file, "bye");
            var tool = new FileSystemTool("fs-del", fileSystem: null);
            var request = new ToolRequest
            {
                Operation = "DeletePath",
                Parameters = new Dictionary<string, object> { { "path", file } }
            };

            var result = await tool.Handle(request);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(File.Exists(file));
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        
        [TestMethod]
        public async Task Handle_MissingPathParameter_ReturnsError()
        {
            // Arrange
            var request = new ToolRequest
            {
                Operation = "ReadFile",
                Parameters = new Dictionary<string, object>()
            };

            // Act
            var result = await _fileSystemTool.Handle(request);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("Missing or invalid 'path' parameter.", result.Error);
        }

        [TestMethod]
        public async Task Handle_UnsupportedOperation_ReturnsError()
        {
            // Arrange
            var request = new ToolRequest { Operation = "DeleteEverything" };

            // Act
            var result = await _fileSystemTool.Handle(request);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("Unsupported operation: DeleteEverything", result.Error);
        }

        [TestMethod]
        public async Task ReceiveAsync_WithToolRequest_CallsHandle()
        {
            // Arrange
            var path = "test.txt";
            var content = "hello world";
            _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(path)).ReturnsAsync(content);
            var toolRequest = new ToolRequest
            {
                Operation = "ReadFile",
                Parameters = new Dictionary<string, object> { { "path", path } }
            };
            var envelope = new MessageEnvelope(toolRequest);

            // Act
            var resultEnvelope = await _fileSystemTool.ReceiveAsync(envelope);
            var toolResult = resultEnvelope.Payload as ToolResult;

            // Assert
            Assert.IsNotNull(toolResult);
            Assert.IsTrue(toolResult.IsSuccess);
            Assert.AreEqual(content, toolResult.Output);
        }

        [TestMethod]
        public async Task ReceiveAsync_WithInvalidPayload_ReturnsError()
        {
            // Arrange
            var envelope = new MessageEnvelope("not a tool request");

            // Act
            var resultEnvelope = await _fileSystemTool.ReceiveAsync(envelope);
            var toolResult = resultEnvelope.Payload as ToolResult;

            // Assert
            Assert.IsNotNull(toolResult);
            Assert.IsFalse(toolResult.IsSuccess);
            Assert.IsTrue(toolResult.Error?.Contains("Unsupported payload", StringComparison.Ordinal) == true);
        }
    }
} 