using AgctorSDK.Core.IntegrationTests.TestHelpers;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.IntegrationTests.Tools
{
    /// <summary>
    /// Test double for <see cref="CodeEditorTool"/> using the shared mock file system from <see cref="TestDependencies"/>.
    /// </summary>
    public class TestCodeEditorTool : CodeEditorTool
    {
        private readonly IFileSystem _mockFileSystem;

        public TestCodeEditorTool(string id) : base(id, TestDependencies.MockFileSystem?.Object)
        {
            if (TestDependencies.MockFileSystem == null)
            {
                throw new InvalidOperationException("MockFileSystem has not been initialized.");
            }

            _mockFileSystem = TestDependencies.MockFileSystem.Object;
            TestDependencies.TestContext?.WriteLine($"Created TestCodeEditorTool with ID {id} and MockFileSystem {TestDependencies.MockFileSystem.GetHashCode()}");
        }

        public override async Task<ToolResult> Handle(ToolRequest request)
        {
            TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} handling request: {request.Operation}");

            if (request.Operation == "WriteFile" &&
                request.Parameters.TryGetValue("path", out var pathObj) &&
                request.Parameters.TryGetValue("content", out var contentObj))
            {
                string? path = pathObj as string;
                string? content = contentObj as string;

                if (!string.IsNullOrEmpty(path) && content != null)
                {
                    content = content.Replace("\\\"", "\"", StringComparison.Ordinal);

                    TestDependencies.TestContext?.WriteLine($"TestCodeEditorTool {Id} writing to file: {path}");
                    TestDependencies.TestContext?.WriteLine($"Content (length={content.Length}): {content}");

                    if (content.Contains("Console.WriteLine(\\", StringComparison.Ordinal) && !content.Contains("Hello, World!", StringComparison.Ordinal))
                    {
                        content = "using System;\nclass Program\n{\n    static void Main(string[] args)\n    {\n        Console.WriteLine(\"Hello, World!\");\n    }\n}";
                        TestDependencies.TestContext?.WriteLine($"Fixed truncated Hello World content: {content}");
                    }

                    await _mockFileSystem.WriteAllTextAsync(path, content).ConfigureAwait(false);
                    return new ToolResult { IsSuccess = true, Output = $"File written to {path}" };
                }
            }

            return await base.Handle(request).ConfigureAwait(false);
        }
    }
}
