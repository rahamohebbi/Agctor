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
using System.Text.Json.Serialization;
using System.IO;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using AgctorSDK.Core.Tools.Implementations.LanguageAdapters;

namespace AgctorSDK.Core.Tools.Implementations
{
    public class CodeEditorTool : Agent, IToolActor
    {
        private readonly IFileSystem _fileSystem;

        public CodeEditorTool(string id) : this(id, new DefaultFileSystem())
        {
        }

        public CodeEditorTool(string id, IFileSystem? fileSystem = null) : base(id)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystem();
        }

        // Fallback: if runtime failed to set ParentAgentId, derive it from the hierarchical ID (parent.child)
        private void EnsureParentId()
        {
            if (ParentAgentId == null)
            {
                var idx = Id.IndexOf('.');
                if (idx > 0)
                {
                    var pid = Id.Substring(0, idx);
                    SetParentAgentId(pid);
                    LogWarning($"ParentAgentId was missing – inferred '{pid}' from hierarchical ID.");
                }
            }
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is ProcessPromptMessage promptMsg)
            {
                await ProcessPromptAsync(promptMsg.Prompt, cancellationToken);
                return new MessageEnvelope(new ToolResult { IsSuccess = true });
            }
            else if (envelope.Payload is ToolRequest request)
            {
                var result = await Handle(request);
                return new MessageEnvelope(result);
            }

            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LogInfo($"CodeEditorTool processing prompt: {prompt}");
            LogInfo($"ParentAgentId = {ParentAgentId ?? "<null>"}");
            LogInfo($"HasFactory={(AgentFactory!=null)} RuntimeAdapterNull={(AgentFactory?.RuntimeAdapter==null)}");

            EnsureParentId();

            try
            {
                var toolRequest = ParsePrompt(prompt);
                LogInfo($"Parsed request: Operation={toolRequest.Operation}, Parameters={string.Join(", ", toolRequest.Parameters.Select(p => $"{p.Key}={p.Value}"))}");
                
                if (toolRequest.Operation == "Error")
                {
                    LogError($"Error parsing tool request: {toolRequest.Parameters["Error"]}");
                    await FinalizeTaskAsFailed(new Exception($"Failed to parse tool request: {toolRequest.Parameters["Error"]}"), cancellationToken);
                    return;
                }
                
                var result = await Handle(toolRequest);
                LogInfo($"Tool execution result: IsSuccess={result.IsSuccess}, Output={result.Output}, Error={result.Error}");

                if (!result.IsSuccess)
                {
                    LogError($"Tool execution failed: {result.Error}");
                    await FinalizeTaskAsFailed(new Exception($"Tool execution failed: {result.Error}"), cancellationToken);
                    return;
                }

                LogInfo("Tool execution succeeded, notifying parent agent");
                await FinalizeTask(result, cancellationToken);
            }
            catch (Exception ex)
            {
                LogError($"Error processing prompt: {ex.Message}");
                await FinalizeTaskAsFailed(new Exception($"Failed to process tool request: {ex.Message}"), cancellationToken);
            }
        }

        public ToolRequest ParsePrompt(string prompt)
        {
            LogInfo($"Parsing prompt for tool request: {prompt}");
            
            // First, extract the command line from the potentially verbose LLM output
            var commandLineMatch = Regex.Match(prompt, @"(CodeEditorTool\s+\w+.*)");
            if (!commandLineMatch.Success)
            {
                LogWarning($"Could not find command line in input: {prompt}");
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Could not find command line in input" } } };
            }

            string commandLine = commandLineMatch.Groups[1].Value.Trim();
            LogInfo($"Found command line: {commandLine}");

            // Now parse the command line
            var match = Regex.Match(commandLine, @"CodeEditorTool\s+(\w+)(.*)");
            if (!match.Success)
            {
                LogWarning($"Could not parse operation from command line: {commandLine}");
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Could not parse operation from command line" } } };
            }

            string operation = match.Groups[1].Value;
            
            // Parse parameters directly from the command line, not just the parameters part
            var parameters = ParseParameters(commandLine);
            
            LogInfo($"Parsed request: Operation={operation}, Parameters={string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"))}");

            var request = new ToolRequest
            {
                Operation = operation,
                Parameters = parameters
            };

            return request;
        }

        private Dictionary<string, object> ParseParameters(string commandLine)
        {
            var parameters = new Dictionary<string, object>();

            // First try to find the path parameter since it's easier to parse
            var pathMatch = Regex.Match(commandLine, @"--path\s+(?:""([^""]*)""|([^\s]+))");
            if (pathMatch.Success)
            {
                string pathValue = pathMatch.Groups[1].Success ? pathMatch.Groups[1].Value : pathMatch.Groups[2].Value;
                parameters["path"] = pathValue;
            }

            // Now for the content parameter which is trickier because it can contain escaped quotes
            // Find where the --content parameter starts
            int contentIndex = commandLine.IndexOf("--content");
            if (contentIndex >= 0)
            {
                // Extract everything after --content (including potential whitespace)
                string contentPart = commandLine.Substring(contentIndex + 9).Trim();
                
                // If the content starts with a quote, extract everything between the opening and closing quotes
                if (contentPart.StartsWith("\""))
                {
                    // Skip the opening quote
                    contentPart = contentPart.Substring(1);
                    
                    // Now build the content until we find the unescaped closing quote
                    var contentBuilder = new System.Text.StringBuilder();
                    bool foundClosingQuote = false;
                    
                    for (int i = 0; i < contentPart.Length; i++)
                    {
                        char c = contentPart[i];
                        
                        // Check for escape sequence
                        if (c == '\\' && i + 1 < contentPart.Length)
                        {
                            // Include the escape character and the next character
                            contentBuilder.Append(c);
                            contentBuilder.Append(contentPart[i + 1]);
                            i++; // Skip the next character since we've already included it
                        }
                        // Check for closing quote
                        else if (c == '"')
                        {
                            foundClosingQuote = true;
                            break;
                        }
                        // Regular character
                        else
                        {
                            contentBuilder.Append(c);
                        }
                    }
                    
                    if (foundClosingQuote || contentBuilder.Length > 0)
                    {
                        string content = contentBuilder.ToString();
                        // Further unescape any escaped quotes in the content
                        content = content.Replace("\\\"", "\"").Replace("\\\\", "\\");
                        parameters["content"] = content;
                    }
                }
                // If not quoted, take the content until the next parameter or end of string
                else
                {
                    var nextParamIndex = contentPart.IndexOf("--");
                    if (nextParamIndex >= 0)
                    {
                        contentPart = contentPart.Substring(0, nextParamIndex).Trim();
                    }
                    
                    parameters["content"] = contentPart;
                }
            }

            // --selector "class:MathUtils > method:Cube"
            var selMatch = Regex.Match(commandLine, @"--selector\s+(?:""([^""]*)""|([^\s]+))");
            if (selMatch.Success)
            {
                string value = selMatch.Groups[1].Success ? selMatch.Groups[1].Value : selMatch.Groups[2].Value;
                parameters["selector"] = value;
            }

            // --anchor "class MathUtils {"
            var anchorMatch = Regex.Match(commandLine, @"--anchor\s+(?:""([^""]*)""|([^\s]+))");
            if (anchorMatch.Success)
            {
                string value = anchorMatch.Groups[1].Success ? anchorMatch.Groups[1].Value : anchorMatch.Groups[2].Value;
                parameters["anchor"] = value;
            }

            // --lineNumber 42
            var lineMatch = Regex.Match(commandLine, @"--lineNumber\s+(\d+)");
            if (lineMatch.Success && int.TryParse(lineMatch.Groups[1].Value, out var ln))
            {
                parameters["lineNumber"] = ln;
            }

            // --startLine 10
            var stMatch = Regex.Match(commandLine, @"--startLine\s+(\d+)");
            if (stMatch.Success && int.TryParse(stMatch.Groups[1].Value, out var stl))
            {
                parameters["startLine"] = stl;
            }

            // --endLine 20
            var endMatch = Regex.Match(commandLine, @"--endLine\s+(\d+)");
            if (endMatch.Success && int.TryParse(endMatch.Groups[1].Value, out var enl))
            {
                parameters["endLine"] = enl;
            }

            // Log the extracted parameters
            var paramString = string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"));
            LogInfo($"Parsed parameters: {paramString}");

            return parameters;
        }

        public override Task<string> AssignSubtaskAsync(string subtaskPrompt, string? agentType = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("CodeEditorTool cannot assign subtasks.");
        }

        public override async Task HandleSubtaskCompletionAsync(string childAgentId, object result, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("CodeEditorTool does not handle subtask completions.");
        }

        public override async Task HandleSubtaskFailureAsync(string childAgentId, Exception error, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("CodeEditorTool does not handle subtask failures.");
        }

        public virtual async Task<ToolResult> Handle(ToolRequest request)
        {
            try
            {
                return request.Operation switch
                {
                    "WriteFile" => await ExecuteWriteFileOperation(request.Parameters),
                    "InsertIntoFile" => await InsertIntoFile(request.Parameters),
                    "ReplaceInFile" => await ReplaceInFile(request.Parameters),
                    "ApplyPatch" => await ReplaceInFile(request.Parameters),
                    _ => new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" }
                };
            }
            catch (Exception ex)
            {
                return new ToolResult { IsSuccess = false, Error = ex.Message };
            }
        }

        private async Task<ToolResult> ExecuteWriteFileOperation(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string filePath)
            {
                return new ToolResult
                {
                    IsSuccess = false,
                    Error = "Path parameter is missing or invalid"
                };
            }

            if (!parameters.TryGetValue("content", out var contentObj) || contentObj is not string content)
            {
                return new ToolResult
                {
                    IsSuccess = false,
                    Error = "Content parameter is missing or invalid"
                };
            }

            // Resolve relative path by searching current directory tree if needed
            if (!Path.IsPathRooted(filePath))
            {
                var cwd = Directory.GetCurrentDirectory();
                var matches = Directory.GetFiles(cwd, filePath, SearchOption.AllDirectories);
                if (matches.Length == 1)
                {
                    LogInfo($"Resolved relative path '{filePath}' to '{matches[0]}'");
                    filePath = matches[0];
                }
                else if (matches.Length == 0)
                {
                    // Treat as new file creation at the relative path
                    LogInfo($"No existing file named '{filePath}' found – will create new file at workspace root.");
                    filePath = Path.Combine(cwd, filePath);
                }
                else
                {
                    return new ToolResult
                    {
                        IsSuccess = false,
                        Error = $"Ambiguous relative path '{filePath}'. Found {matches.Length} matches. Please specify full path."
                    };
                }
            }

            // If the file doesn't exist yet, treat this as create + write instead of erroring out.
            bool creatingNew = !File.Exists(filePath);

            LogInfo($"Overwriting file: {filePath}");
            LogInfo($"Content sample (first 100 chars): {content.Substring(0, Math.Min(100, content.Length))}");

            try
            {
                // Ensure the directory exists
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Remove any extra wrapping quotes that might have been added during parameter extraction
                content = content.Trim();
                // Some LLMs wrap the entire snippet in one or more quote characters – peel them repeatedly.
                while (content.Length > 1 && content.StartsWith("\"") && content.EndsWith("\""))
                {
                    content = content.Substring(1, content.Length - 2).Trim();
                }

                // Strip Markdown code fences (``` or ```csharp etc.) if present
                if (content.StartsWith("```"))
                {
                    var newlineIdx = content.IndexOf('\n');
                    if (newlineIdx >= 0)
                    {
                        // Skip the first line (``` or ```lang)
                        content = content.Substring(newlineIdx + 1);
                    }
                    // Remove ending fence
                    var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
                    if (lastFence >= 0)
                    {
                        content = content.Substring(0, lastFence);
                    }
                    content = content.Trim();
                }

                // Remove stray leading backticks or colons that sometimes remain after fence removal
                content = content.Trim('`').TrimStart(':',' ');

                // Clean up any remaining escaped quotes and escaped newlines/tabs
                content = content.Replace("\\\"", "\"")
                                 .Replace("\"\"", "\"")
                                 .Replace("\\n", System.Environment.NewLine)
                                 .Replace("\\r", "")
                                 .Replace("\\t", "\t");
                
                // Language-specific formatter
                var adapterFmt = LanguageAdapterFactory.GetByExtension(Path.GetExtension(filePath));
                if (adapterFmt != null)
                {
                    var (ok, formatted) = await adapterFmt.TryFormatAsync(content, default);
                    if (ok && formatted != null) content = formatted;
                }
                
                // Simple brace-balance fix: if more '{' than '}', append the missing ones.
                int openBraces = content.Count(c => c == '{');
                int closeBraces = content.Count(c => c == '}');
                if (openBraces > closeBraces)
                {
                    var diff = openBraces - closeBraces;
                    LogInfo($"Brace balance check: adding {diff} missing '}}' characters");
                    content += System.Environment.NewLine + new string('}', diff);
                }
                
                LogInfo($"Final content to write (first 100 chars): {content.Substring(0, Math.Min(100, content.Length))}");

                // Write the file
                await File.WriteAllTextAsync(filePath, content);
                
                // Verify content was written correctly
                string writtenContent = await File.ReadAllTextAsync(filePath);
                LogInfo($"Content verification (first 100 chars): {writtenContent.Substring(0, Math.Min(100, writtenContent.Length))}");

                return new ToolResult
                {
                    IsSuccess = true,
                    Output = $"File written to {filePath}"
                };
            }
            catch (Exception ex)
            {
                LogError($"Error writing file: {ex.Message}");
                return new ToolResult
                {
                    IsSuccess = false,
                    Error = $"Error writing file: {ex.Message}"
                };
            }
        }

        private async Task<ToolResult> InsertIntoFile(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." };
            if (!parameters.TryGetValue("content", out var contentObj) || contentObj is not string content)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'content' parameter." };

            content = NormalizeContent(content);

            string source = await _fileSystem.ReadAllTextAsync(path);

            // 1) Try selector via language adapter
            if (parameters.TryGetValue("selector", out var selObj) && selObj is string selector)
            {
                var adapter = LanguageAdapterFactory.GetByExtension(Path.GetExtension(path));
                if (adapter != null)
                {
                    try
                    {
                        var updated = adapter.InsertBySelector(source, selector, content);
                        if (updated != null)
                        {
                            await _fileSystem.WriteAllTextAsync(path, updated);
                            return new ToolResult { IsSuccess = true, Output = $"File written to {path}" };
                        }
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"Adapter insertion failed: {ex.Message}. Falling back.");
                    }
                }
            }

            // 2) Try anchor text
            if (parameters.TryGetValue("anchor", out var ancObj) && ancObj is string anchor)
            {
                int idx = source.IndexOf(anchor, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    int insertPos = idx + anchor.Length;
                    var updated = source.Insert(insertPos, System.Environment.NewLine + content);
                    await _fileSystem.WriteAllTextAsync(path, updated);
                    return new ToolResult { IsSuccess = true, Output = $"File written to {path}" };
                }
                LogWarning($"Anchor '{anchor}' not found. Falling back.");
            }

            // 3) Fallback to lineNumber
            if (parameters.TryGetValue("lineNumber", out var lineObj) && int.TryParse(lineObj.ToString(), out var lineNumber))
            {
                var lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
                if (lineNumber < 0 || lineNumber > lines.Count)
                    return new ToolResult { IsSuccess = false, Error = "Line number is out of range." };
                lines.Insert(lineNumber, content);
                var updated = string.Join(System.Environment.NewLine, lines);
                await _fileSystem.WriteAllTextAsync(path, updated);
                return new ToolResult { IsSuccess = true, Output = $"File written to {path}" };
            }

            return new ToolResult { IsSuccess = false, Error = "Could not insert content – no valid selector, anchor or line number resolved." };
        }

        private async Task<ToolResult> ReplaceInFile(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." };
            if (!parameters.TryGetValue("content", out var contentObj) || contentObj is not string content)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'content' parameter." };

            content = NormalizeContent(content);

            string source = await _fileSystem.ReadAllTextAsync(path);

            // Prefer selector replacement via adapter
            if (parameters.TryGetValue("selector", out var selectorObj) && selectorObj is string selector2)
            {
                var adapter = LanguageAdapterFactory.GetByExtension(Path.GetExtension(path));
                if (adapter != null)
                {
                    try
                    {
                        var updated = adapter.ReplaceBySelector(source, selector2, content);
                        if (updated != null)
                        {
                            await _fileSystem.WriteAllTextAsync(path, updated);
                            return new ToolResult { IsSuccess = true, Output = $"File written to {path}" };
                        }
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"Adapter replacement failed: {ex.Message}");
                    }
                }
            }

            // Anchor-based replacement (replace first occurrence only)
            if (parameters.TryGetValue("anchor", out var ancObj) && ancObj is string anchor)
            {
                int idx = source.IndexOf(anchor, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    int endIdx = idx + anchor.Length;
                    var updated = source.Substring(0, idx) + content + source.Substring(endIdx);
                    await _fileSystem.WriteAllTextAsync(path, updated);
                    return new ToolResult { IsSuccess = true, Output = $"File written to {path}" };
                }
            }

            // Fallback to explicit line numbers
            if (parameters.TryGetValue("startLine", out var startLineObj) && int.TryParse(startLineObj.ToString(), out var startLine) &&
                parameters.TryGetValue("endLine", out var endLineObj) && int.TryParse(endLineObj.ToString(), out var endLine))
            {
                var lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

                if (startLine < 0 || startLine > lines.Count || endLine < startLine || endLine > lines.Count)
                    return new ToolResult { IsSuccess = false, Error = "Line numbers are out of range." };

                lines.RemoveRange(startLine, endLine - startLine);
                lines.Insert(startLine, content);

                var updated = string.Join(System.Environment.NewLine, lines);
                await _fileSystem.WriteAllTextAsync(path, updated);
                return new ToolResult { IsSuccess = true, Output = $"File written to {path}" };
            }

            return new ToolResult { IsSuccess = false, Error = "Could not replace content – no valid selector, anchor or line numbers resolved." };
        }

        #region Roslyn helpers

        /// <summary>
        /// Inserts <paramref name="snippet"/> right after the node matched by <paramref name="selector"/>.
        /// Currently supports simple selectors like "class:Foo" or "class:Foo > method:Bar".
        /// Returns null when selector not resolved.
        /// </summary>
        private string? RoslynInsertBySelector(string source, string selector, string snippet)
        {
            try
            {
                var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
                var root = tree.GetRoot();

                var target = ResolveSelector(root, selector);
                if (target == null) return null;

                // Insert after target node
                int insertPos = target.Span.End;
                var updated = source.Insert(insertPos, System.Environment.NewLine + snippet + System.Environment.NewLine);
                return updated;
            }
            catch (Exception ex)
            {
                LogWarning($"Roslyn insertion failed: {ex.Message}");
                return null;
            }
        }

        private Microsoft.CodeAnalysis.SyntaxNode? ResolveSelector(Microsoft.CodeAnalysis.SyntaxNode root, string selector)
        {
            // Very simple selector parser: segment1 > segment2
            var segments = selector.Split('>');
            Microsoft.CodeAnalysis.SyntaxNode? current = root;
            foreach (var rawSeg in segments)
            {
                var seg = rawSeg.Trim();
                var parts = seg.Split(':');
                if (parts.Length != 2) return null;
                var kind = parts[0].Trim().ToLowerInvariant();
                var name = parts[1].Trim();

                if (kind == "class")
                {
                    current = current.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                                     .FirstOrDefault(c => c.Identifier.Text == name);
                }
                else if (kind == "method")
                {
                    current = current.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                                     .FirstOrDefault(m => m.Identifier.Text == name);
                }
                else if (kind == "namespace")
                {
                    current = current.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax>()
                                     .FirstOrDefault(n => n.Name.ToString() == name);
                }
                else
                {
                    // Unsupported kind
                    return null;
                }

                if (current == null) return null;
            }

            return current;
        }

        /// <summaryNormalize content sent by LLM: strip fences/backticks, unescape sequences, balance braces.</summary>
        private string NormalizeContent(string content)
        {
            content = content.Trim();

            // Remove wrapping quotes/backticks repeatedly
            while (content.Length > 1 && (content.StartsWith("\"") && content.EndsWith("\"") || content.StartsWith("`") && content.EndsWith("`")))
            {
                content = content.Substring(1, content.Length - 2).Trim();
            }

            // Strip markdown code fences
            if (content.StartsWith("```"))
            {
                var nl = content.IndexOf('\n');
                if (nl >= 0) content = content.Substring(nl + 1);
                var last = content.LastIndexOf("```", StringComparison.Ordinal);
                if (last >= 0) content = content.Substring(0, last);
                content = content.Trim();
            }

            // Unescape common sequences
            content = content.Replace("\\n", System.Environment.NewLine)
                           .Replace("\\r", "")
                           .Replace("\\t", "\t")
                           .Replace("\\\"", "\"");

            // Balance braces simple check
            int open = content.Count(c => c == '{');
            int close = content.Count(c => c == '}');
            if (open > close) content += new string('}', open - close);

            return content;
        }

        #endregion
    }
} 