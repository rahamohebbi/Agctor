using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Tools.Implementations
{
    public class CodeEditorTool : BaseActor, IToolActor
    {
        private readonly IFileSystem _fileSystem;

        public CodeEditorTool(string id, IFileSystem? fileSystem = null) : base(id, "CodeEditorTool")
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
                    "WriteFile" => await WriteFile(request.Parameters),
                    "InsertIntoFile" => await InsertIntoFile(request.Parameters),
                    "ReplaceInFile" => await ReplaceInFile(request.Parameters),
                    _ => new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" }
                };
            }
            catch (Exception ex)
            {
                return new ToolResult { IsSuccess = false, Error = ex.Message };
            }
        }

        private async Task<ToolResult> WriteFile(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." };
            if (!parameters.TryGetValue("content", out var contentObj) || contentObj is not string content)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'content' parameter." };

            await _fileSystem.WriteAllTextAsync(path, content);
            return new ToolResult { IsSuccess = true };
        }

        private async Task<ToolResult> InsertIntoFile(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." };
            if (!parameters.TryGetValue("content", out var contentObj) || contentObj is not string content)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'content' parameter." };
            if (!parameters.TryGetValue("lineNumber", out var lineObj) || !int.TryParse(lineObj.ToString(), out var lineNumber))
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'lineNumber' parameter." };

            var lines = (await _fileSystem.ReadAllLinesAsync(path)).ToList();
            if (lineNumber < 0 || lineNumber > lines.Count)
                return new ToolResult { IsSuccess = false, Error = "Line number is out of range." };

            lines.Insert(lineNumber, content);
            await _fileSystem.WriteAllLinesAsync(path, lines);
            return new ToolResult { IsSuccess = true };
        }

        private async Task<ToolResult> ReplaceInFile(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." };
            if (!parameters.TryGetValue("content", out var contentObj) || contentObj is not string content)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'content' parameter." };
            if (!parameters.TryGetValue("startLine", out var startLineObj) || !int.TryParse(startLineObj.ToString(), out var startLine))
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'startLine' parameter." };
            if (!parameters.TryGetValue("endLine", out var endLineObj) || !int.TryParse(endLineObj.ToString(), out var endLine))
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'endLine' parameter." };

            var lines = (await _fileSystem.ReadAllLinesAsync(path)).ToList();

            if (startLine < 0 || startLine > lines.Count || endLine < startLine || endLine > lines.Count)
                return new ToolResult { IsSuccess = false, Error = "Line numbers are out of range." };

            lines.RemoveRange(startLine, endLine - startLine);
            lines.Insert(startLine, content);

            await _fileSystem.WriteAllLinesAsync(path, lines);
            return new ToolResult { IsSuccess = true };
        }
    }
} 