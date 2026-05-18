using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Tools.Implementations
{
    // TODO: Add security checks for file system access.
    [AgctorHostTool("file-system", "File System Tool", "Performs file system operations like read, write, list directories")]
    public class FileSystemTool : ToolActorBase
    {
        private readonly IFileSystem _fileSystem;

        public FileSystemTool(string id, IFileSystem? fileSystem = null) : base(id, "FileSystemTool")
        {
            _fileSystem = fileSystem ?? new DefaultFileSystem();
        }

        protected override Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(new ToolResult { IsSuccess = false, Error = "FileSystemTool expects a ToolRequest payload." });

        public override async Task<ToolResult> Handle(ToolRequest request)
        {
            try
            {
                return request.Operation switch
                {
                    "ReadFile" => await ReadFile(request.Parameters),
                    "WriteFile" => await WriteFile(request.Parameters),
                    // Structured callers (HTTP bridge, tests) use these names; align with ToolRequest style.
                    "ListDirectory" => await ListDirectoryAsync(request.Parameters),
                    "DeletePath" => await DeletePathAsync(request.Parameters),
                    _ => new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" }
                };
            }
            catch (Exception ex)
            {
                return new ToolResult { IsSuccess = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Lists file and directory names under <c>path</c> (creates the directory if it does not exist, matching prior host HTTP behavior).
        /// </summary>
        private Task<ToolResult> ListDirectoryAsync(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
            {
                return Task.FromResult(new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." });
            }

            var resolved = Path.GetFullPath(path);
            var targetDir = Directory.Exists(resolved)
                ? resolved
                : Path.GetDirectoryName(resolved) ?? resolved;
            Directory.CreateDirectory(targetDir);
            var entries = Directory.EnumerateFileSystemEntries(targetDir)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            var payload = JsonSerializer.Serialize(new { path = targetDir, entries });
            return Task.FromResult(new ToolResult { IsSuccess = true, Output = payload });
        }

        /// <summary>Deletes a file or directory (recursive for directories).</summary>
        private Task<ToolResult> DeletePathAsync(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
            {
                return Task.FromResult(new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." });
            }

            var resolved = Path.GetFullPath(path);
            var deleted = false;
            if (File.Exists(resolved))
            {
                File.Delete(resolved);
                deleted = true;
            }
            else if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
                deleted = true;
            }

            var payload = JsonSerializer.Serialize(new { path = resolved, deleted });
            return Task.FromResult(new ToolResult { IsSuccess = true, Output = payload });
        }

        private async Task<ToolResult> ReadFile(System.Collections.Generic.IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
            {
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." };
            }

            var content = await _fileSystem.ReadAllTextAsync(path);
            return new ToolResult { IsSuccess = true, Output = content };
        }

        private async Task<ToolResult> WriteFile(System.Collections.Generic.IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
            {
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." };
            }

            if (!parameters.TryGetValue("content", out var contentObj) || contentObj is not string content)
            {
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'content' parameter." };
            }

            await _fileSystem.WriteAllTextAsync(path, content);
            return new ToolResult { IsSuccess = true };
        }
    }
} 