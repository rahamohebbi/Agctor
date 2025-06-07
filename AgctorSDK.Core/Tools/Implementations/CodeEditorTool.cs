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
using System.Text.RegularExpressions;

namespace AgctorSDK.Core.Tools.Implementations
{
    public class CodeEditorTool : Agent, IToolActor
    {
        private readonly IFileSystem _fileSystem;

        public string? CurrentPrompt { get; private set; }
        public string? ParentAgentId { get; private set; }
        public IReadOnlyList<string> ChildAgentIds => new List<string>().AsReadOnly();
        public AgentStatus Status { get; private set; }
        public int HierarchyDepth { get; private set; }

        public event EventHandler<AgentStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<ChildAgentSpawnedEventArgs>? ChildAgentSpawned;
        public event EventHandler<SubtaskCompletedEventArgs>? SubtaskCompleted;

        public CodeEditorTool(string id, IFileSystem? fileSystem = null) : base(id)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystem();
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is ProcessPromptMessage promptMsg)
            {
                await ProcessPromptAsync(promptMsg.Prompt, cancellationToken);
                
                // Return a successful acknowledgment
                return new MessageEnvelope(new ToolResult { IsSuccess = true });
            }
            else if (envelope.Payload is ToolRequest request)
            {
                var result = await Handle(request);
                return new MessageEnvelope(result);
            }

            // Call base for handling other message types
            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            await base.ProcessPromptAsync(prompt, cancellationToken);
            
            try 
            {
                var toolRequest = ParsePromptToToolRequest(prompt);
                var result = await Handle(toolRequest);

                if (ParentAgentId != null && AgentFactory?.RuntimeAdapter != null)
                {
                    var completionMessage = new SubtaskCompletedMessage(Id, ParentAgentId, result);
                    var envelope = new MessageEnvelope(completionMessage);
                    await AgentFactory.RuntimeAdapter.SendMessageAsync(ParentAgentId, envelope, cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing prompt: {ex.Message}");
                
                if (ParentAgentId != null && AgentFactory?.RuntimeAdapter != null)
                {
                    var failureMessage = new SubtaskFailedMessage(Id, ParentAgentId, new Exception($"Failed to process tool request: {ex.Message}"));
                    var envelope = new MessageEnvelope(failureMessage);
                    await AgentFactory.RuntimeAdapter.SendMessageAsync(ParentAgentId, envelope, cancellationToken: cancellationToken);
                }
            }
        }

        public virtual ToolRequest ParsePromptToToolRequest(string prompt)
        {
            // Extract command and parameters
            var commandParts = new List<string>();
            var inQuotes = false;
            var currentPart = "";
            
            for (int i = 0; i < prompt.Length; i++)
            {
                var c = prompt[i];
                
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(currentPart))
                    {
                        commandParts.Add(currentPart);
                        currentPart = "";
                    }
                }
                else
                {
                    currentPart += c;
                }
            }
            
            if (!string.IsNullOrEmpty(currentPart))
            {
                commandParts.Add(currentPart);
            }

            if (commandParts.Count == 0)
            {
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Empty prompt" } } };
            }

            var request = new ToolRequest
            {
                Operation = commandParts[0],
                Parameters = new Dictionary<string, object>()
            };

            for (int i = 1; i < commandParts.Count; i++)
            {
                if (commandParts[i].StartsWith("--") && i + 1 < commandParts.Count)
                {
                    var key = commandParts[i].Substring(2); // Remove the -- prefix
                    var value = commandParts[i + 1];
                    request.Parameters[key] = value;
                    i++; // Skip the value
                }
            }

            return request;
        }

        public Task<string> AssignSubtaskAsync(string subtaskPrompt, string? agentType = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("CodeEditorTool cannot assign subtasks.");
        }

        public void SetAgentFactory(IAgentFactory agentFactory)
        {
            // This tool does not use an agent factory.
        }

        public void SetParentAgentId(string? parentAgentId)
        {
            ParentAgentId = parentAgentId;
        }

        public void SetHierarchyDepth(int depth)
        {
            HierarchyDepth = depth;
        }

        public async Task HandleSubtaskCompletionAsync(string childAgentId, object result, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("CodeEditorTool does not handle subtask completions.");
        }

        public async Task HandleSubtaskFailureAsync(string childAgentId, Exception error, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("CodeEditorTool does not handle subtask failures.");
        }

        public virtual async Task<ToolResult> Handle(ToolRequest request)
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