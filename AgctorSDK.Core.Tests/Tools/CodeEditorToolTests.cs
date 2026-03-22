using System;
using System.Collections.Generic;
using System.IO;
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
        }

        [TestMethod]
        public async Task Handle_InsertIntoFile_LineNumber_Success()
        {
            var original = "line 1\nline 3";
            _mockFileSystem.Setup(fs => fs.ReadAllTextAsync("a.txt")).ReturnsAsync(original);

            var request = new ToolRequest
            {
                Operation = "InsertIntoFile",
                Parameters = new Dictionary<string, object>
                {
                    { "path", "a.txt" },
                    { "content", "line 2" },
                    { "lineNumber", 1 }
                }
            };

            var result = await _codeEditorTool.Handle(request);

            Assert.IsTrue(result.IsSuccess);
            _mockFileSystem.Verify(fs => fs.WriteAllTextAsync("a.txt", "line 1\nline 2\nline 3"), Times.Once);
        }

        [TestMethod]
        public async Task Handle_InsertIntoFile_Selector_Success()
        {
            var source = @"namespace Demo { public static class MathUtils { public static int Square(int x) => x * x; } }";
            _mockFileSystem.Setup(fs => fs.ReadAllTextAsync("MathUtils.cs")).ReturnsAsync(source);

            const string snippet = "public static int Cube(int x) => x * x * x;";

            var request = new ToolRequest
            {
                Operation = "InsertIntoFile",
                Parameters = new Dictionary<string, object>
                {
                    { "path", "MathUtils.cs" },
                    { "content", snippet },
                    { "selector", "class:MathUtils" }
                }
            };

            string? captured = null;
            _mockFileSystem.Setup(fs => fs.WriteAllTextAsync("MathUtils.cs", It.IsAny<string>()))
                           .Callback<string,string>((_, txt) => captured = txt)
                           .Returns(Task.CompletedTask);

            var result = await _codeEditorTool.Handle(request);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(captured);
            Assert.IsTrue(captured!.Contains(snippet));
        }

        [TestMethod]
        public async Task Handle_ReplaceInFile_Selector_Success()
        {
            var source = @"namespace Demo { public static class MathUtils { public static int Square(int x) => x * x; } }";
            _mockFileSystem.Setup(fs => fs.ReadAllTextAsync("MathUtils.cs")).ReturnsAsync(source);

            const string newSquare = "public static int Square(int x) => x * x * 2;";

            var request = new ToolRequest
            {
                Operation = "ReplaceInFile",
                Parameters = new Dictionary<string, object>
                {
                    { "path", "MathUtils.cs" },
                    { "content", newSquare },
                    { "selector", "class:MathUtils > method:Square" }
                }
            };

            string? captured = null;
            _mockFileSystem.Setup(fs => fs.WriteAllTextAsync("MathUtils.cs", It.IsAny<string>()))
                           .Callback<string,string>((_, txt) => captured = txt)
                           .Returns(Task.CompletedTask);

            var result = await _codeEditorTool.Handle(request);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(captured);
            Assert.IsTrue(captured!.Contains(newSquare));
            Assert.IsFalse(captured.Contains("=> x * x;"));
        }

        [TestMethod]
        public async Task Handle_ReplaceInFile_Success()
        {
            var initialLines = new[] { "line 1", "line 2", "line 3" };
            _mockFileSystem.Setup(fs => fs.ReadAllTextAsync("a.txt")).ReturnsAsync(string.Join("\n", initialLines));
            var request = new ToolRequest
            {
                Operation = "ReplaceInFile",
                Parameters = new Dictionary<string, object> { { "path", "a.txt" }, { "content", "new line" }, { "startLine", 1 }, { "endLine", 2 } }
            };

            var result = await _codeEditorTool.Handle(request);

            Assert.IsTrue(result.IsSuccess);
            _mockFileSystem.Verify(fs => fs.WriteAllTextAsync("a.txt", "line 1\nnew line\nline 3"), Times.Once);
        }
        
        [TestMethod]
        public async Task Handle_InsertIntoFile_InvalidLineNumber_ReturnsError()
        {
            var initialLines = new[] { "line 1", "line 2" };
            _mockFileSystem.Setup(fs => fs.ReadAllTextAsync("a.txt")).ReturnsAsync(string.Join("\n", initialLines));
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
        public async Task Handle_InsertIntoFile_MissingFileNoPlacement_CreatesViaFallback()
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "agctor-editor-insert-fallback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            var prev = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(sandbox);
                var tool = new CodeEditorTool("t", new DefaultFileSystem());
                var request = new ToolRequest
                {
                    Operation = "InsertIntoFile",
                    Parameters = new Dictionary<string, object>
                    {
                        { "path", "documentation/project.md" },
                        { "content", "# Doc\n" }
                    }
                };
                var result = await tool.Handle(request);
                Assert.IsTrue(result.IsSuccess, result.Error);
                var full = Path.Combine(sandbox, "documentation", "project.md");
                Assert.IsTrue(File.Exists(full));
                StringAssert.Contains(await File.ReadAllTextAsync(full), "Doc");
            }
            finally
            {
                Directory.SetCurrentDirectory(prev);
                try
                {
                    if (Directory.Exists(sandbox))
                        Directory.Delete(sandbox, true);
                }
                catch
                {
                    // best-effort
                }
            }
        }

        [TestMethod]
        public async Task Handle_InsertIntoFile_MissingFileWithSelector_StillErrors()
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "agctor-editor-insert-sel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            var prev = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(sandbox);
                var tool = new CodeEditorTool("t", new DefaultFileSystem());
                var request = new ToolRequest
                {
                    Operation = "InsertIntoFile",
                    Parameters = new Dictionary<string, object>
                    {
                        { "path", "Missing.cs" },
                        { "content", "void F(){}" },
                        { "selector", "class:Foo" }
                    }
                };
                var result = await tool.Handle(request);
                Assert.IsFalse(result.IsSuccess);
                StringAssert.Contains(result.Error ?? "", "Could not find file");
            }
            finally
            {
                Directory.SetCurrentDirectory(prev);
                try
                {
                    if (Directory.Exists(sandbox))
                        Directory.Delete(sandbox, true);
                }
                catch
                {
                    // best-effort
                }
            }
        }

        [TestMethod]
        public async Task Handle_ReplaceInFile_InvalidLineRange_ReturnsError()
        {
            var initialLines = new[] { "line 1", "line 2", "line 3" };
            _mockFileSystem.Setup(fs => fs.ReadAllTextAsync("a.txt")).ReturnsAsync(string.Join("\n", initialLines));
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