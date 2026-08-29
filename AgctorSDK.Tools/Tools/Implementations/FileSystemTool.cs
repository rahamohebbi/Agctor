using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Tools.Implementations
{
    /// <summary>
    /// File read/write tool actor. Paths are not sandboxed: this tool can touch any
    /// location the process can access. Inject a rooted <see cref="IFileSystem"/> or
    /// only host it in trusted environments. See SECURITY.md.
    /// </summary>
    public class FileSystemTool : BaseActor, IToolActor
    {
        private readonly IFileSystem _fileSystem;

        public FileSystemTool(string id, IFileSystem? fileSystem = null) : base(id, "FileSystemTool")
        {
            _fileSystem = fileSystem ?? new DefaultFileSystem();
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is ToolRequest request)
            {
                var result = await Handle(request);
                return new MessageEnvelope(result);
            }

            var errorResult = new ToolResult
            {
                IsSuccess = false,
                Error = "Invalid payload. Expected ToolRequest."
            };
            return new MessageEnvelope(errorResult);
        }

        public async Task<ToolResult> Handle(ToolRequest request)
        {
            try
            {
                return request.Operation switch
                {
                    "ReadFile" => await ReadFile(request.Parameters),
                    "WriteFile" => await WriteFile(request.Parameters),
                    _ => new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" }
                };
            }
            catch (Exception ex)
            {
                return new ToolResult { IsSuccess = false, Error = ex.Message };
            }
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