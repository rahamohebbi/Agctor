using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AgctorSDK.Core.Tests.Tools
{
    [TestClass]
    public class CodeEditorToolTests
    {
        private Mock<IFileSystem> _mockFileSystem;
        private CodeEditorTool _codeEditorTool;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockFileSystem = new Mock<IFileSystem>();
            _codeEditorTool = new CodeEditorTool("test-editor-tool", _mockFileSystem.Object);
        }

        [TestMethod]
        public async Task Handle_WriteFile_Success()
        {
            var request = new ToolRequest
            {
                Operation = "WriteFile",
                Parameters = new Dictionary<string, object> { { "path", "a.txt" }, { "content", "new content" } }
            };

            var result = await _codeEditorTool.Handle(request);

            Assert.IsTrue(result.IsSuccess);
            _mockFileSystem.Verify(fs => fs.WriteAllTextAsync("a.txt", "new content"), Times.Once);
        }

        [TestMethod]
        public async Task Handle_InsertIntoFile_Success()
        {
            var initialLines = new[] { "line 1", "line 3" };
            _mockFileSystem.Setup(fs => fs.ReadAllLinesAsync("a.txt")).ReturnsAsync(initialLines);
            var request = new ToolRequest
            {
                Operation = "InsertIntoFile",
                Parameters = new Dictionary<string, object> { { "path", "a.txt" }, { "content", "line 2" }, { "lineNumber", 1 } }
            };

            var result = await _codeEditorTool.Handle(request);

            Assert.IsTrue(result.IsSuccess);
            _mockFileSystem.Verify(fs => fs.WriteAllLinesAsync("a.txt", It.Is<IEnumerable<string>>(lines =>
                lines.SequenceEqual(new[] { "line 1", "line 2", "line 3" })
            )), Times.Once);
        }

        [TestMethod]
        public async Task Handle_ReplaceInFile_Success()
        {
            var initialLines = new[] { "line 1", "line 2", "line 3" };
            _mockFileSystem.Setup(fs => fs.ReadAllLinesAsync("a.txt")).ReturnsAsync(initialLines);
            var request = new ToolRequest
            {
                Operation = "ReplaceInFile",
                Parameters = new Dictionary<string, object> { { "path", "a.txt" }, { "content", "new line" }, { "startLine", 1 }, { "endLine", 2 } }
            };

            var result = await _codeEditorTool.Handle(request);

            Assert.IsTrue(result.IsSuccess);
            _mockFileSystem.Verify(fs => fs.WriteAllLinesAsync("a.txt", It.Is<IEnumerable<string>>(lines =>
                lines.SequenceEqual(new[] { "line 1", "new line", "line 3" })
            )), Times.Once);
        }
        
        [TestMethod]
        public async Task Handle_InsertIntoFile_InvalidLineNumber_ReturnsError()
        {
            var initialLines = new[] { "line 1", "line 2" };
            _mockFileSystem.Setup(fs => fs.ReadAllLinesAsync("a.txt")).ReturnsAsync(initialLines);
            var request = new ToolRequest
            {
                Operation = "InsertIntoFile",
                Parameters = new Dictionary<string, object> { { "path", "a.txt" }, { "content", "line 3" }, { "lineNumber", 5 } }
            };

            var result = await _codeEditorTool.Handle(request);

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("Line number is out of range.", result.Error);
        }

        [TestMethod]
        public async Task Handle_ReplaceInFile_InvalidLineRange_ReturnsError()
        {
            var initialLines = new[] { "line 1", "line 2", "line 3" };
            _mockFileSystem.Setup(fs => fs.ReadAllLinesAsync("a.txt")).ReturnsAsync(initialLines);
            var request = new ToolRequest
            {
                Operation = "ReplaceInFile",
                Parameters = new Dictionary<string, object> { { "path", "a.txt" }, { "content", "new" }, { "startLine", 2 }, { "endLine", 1 } }
            };

            var result = await _codeEditorTool.Handle(request);
            
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("Line numbers are out of range.", result.Error);
        }
    }
} 