using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Tools.LanguageExecutors;

namespace AgctorSDK.Core.Tools.Implementations
{
    public class CodeExecutorTool : ToolActorBase
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILanguageExecutorFactory _executorFactory;

        public CodeExecutorTool(string id) : this(id, new DefaultFileSystem(), new LanguageExecutorFactory())
        {
        }

        public CodeExecutorTool(string id, IFileSystem? fileSystem = null, ILanguageExecutorFactory? executorFactory = null) : base(id, "CodeExecutorTool")
        {
            _fileSystem = fileSystem ?? new DefaultFileSystem();
            _executorFactory = executorFactory ?? new LanguageExecutorFactory();
        }

        protected override async Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken)
        {
            LogInfo($"CodeExecutorTool processing prompt: {prompt}");

            try
            {
                var toolRequest = ParsePrompt(prompt);
                LogInfo($"Parsed request: Operation={toolRequest.Operation}, Parameters={string.Join(", ", toolRequest.Parameters.Select(p => $"{p.Key}={p.Value}"))}");

                if (toolRequest.Operation == "Error")
                {
                    LogWarning($"Tool request parse skipped: {toolRequest.Parameters["Error"]}. Expected a line like: CodeExecutorTool RunCode --language python --code \"...\"");
                    return new ToolResult { IsSuccess = false, Error = toolRequest.Parameters["Error"]?.ToString() ?? "Parse error" };
                }

                var result = await Handle(toolRequest);
                LogInfo($"Tool execution result: IsSuccess={result.IsSuccess}, Output={result.Output}, Error={result.Error}");
                return result;
            }
            catch (Exception ex)
            {
                LogError($"Error processing prompt: {ex.Message}");
                return new ToolResult { IsSuccess = false, Error = $"Failed to process tool request: {ex.Message}" };
            }
        }

        public ToolRequest ParsePrompt(string prompt)
        {
            LogInfo($"Parsing prompt for tool request: {prompt}");
            var text = prompt ?? string.Empty;

            // LLMs often wrap prose/newlines/casing; match case-insensitive and allow newlines inside quoted --code.
            var commandLineMatch = System.Text.RegularExpressions.Regex.Match(
                text.Trim(),
                @"(?is)CodeExecutorTool\s+\w+.*");
            string commandLine;
            if (commandLineMatch.Success)
            {
                commandLine = commandLineMatch.Value.Trim();
            }
            else
            {
                // Some callers emit only "RunCode --language ..." without the tool prefix.
                var runOnly = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"(?is)\b(RunCode\b.*)");
                if (!runOnly.Success)
                {
                    LogWarning($"Could not find command line in input (expected 'CodeExecutorTool RunCode ...' or 'RunCode ...'): {(text.Length > 400 ? text[..400] + "…" : text)}");
                    return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Could not find command line in input" } } };
                }

                commandLine = ("CodeExecutorTool " + runOnly.Groups[1].Value).Trim();
            }

            LogInfo($"Found command line: {commandLine}");

            // Now parse the command line
            var match = System.Text.RegularExpressions.Regex.Match(commandLine, @"(?i)CodeExecutorTool\s+(\w+)(.*)");
            if (!match.Success)
            {
                LogWarning($"Could not parse operation from command line: {commandLine}");
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Could not parse operation from command line" } } };
            }

            string operation = match.Groups[1].Value;
            
            // Parse parameters directly from the command line
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
            var pathMatch = System.Text.RegularExpressions.Regex.Match(commandLine, @"--path\s+(?:""([^""]*)""|([^\s--][^\s]*))");
            if (pathMatch.Success)
            {
                string pathValue = pathMatch.Groups[1].Success ? pathMatch.Groups[1].Value : pathMatch.Groups[2].Value;
                parameters["path"] = pathValue;
            }

            // Look for the code parameter
            var codeMatch = System.Text.RegularExpressions.Regex.Match(commandLine, @"--code\s+(?:""([^""]*(?:(?:\\""|"""")[^""]*)*)""|([^\s--][^\s]*))");
            if (codeMatch.Success)
            {
                string codeValue = codeMatch.Groups[1].Success ? codeMatch.Groups[1].Value : codeMatch.Groups[2].Value;
                // Clean up escaped quotes
                codeValue = codeValue.Replace("\\\"", "\"").Replace("\"\"", "\"");
                parameters["code"] = codeValue;
            }

            // Look for the language parameter
            var languageMatch = System.Text.RegularExpressions.Regex.Match(commandLine, @"--language\s+(?:""([^""]*)""|([^\s--][^\s]*))");
            if (languageMatch.Success)
            {
                string languageValue = languageMatch.Groups[1].Success ? languageMatch.Groups[1].Value : languageMatch.Groups[2].Value;
                parameters["language"] = languageValue;
            }

            return parameters;
        }

        public override async Task<ToolResult> Handle(ToolRequest request)
        {
            return request.Operation switch
            {
                "RunCode" => await RunCodeAsync(request.Parameters),
                "RunFile" => await RunFileAsync(request.Parameters),
                // Keep backward compatibility
                "RunCSharpCode" => await RunCodeAsync(request.Parameters, "csharp"),
                "RunCSharpFile" => await RunFileAsync(request.Parameters, "csharp"),
                _ => new ToolResult
                {
                    IsSuccess = false,
                    Error = $"Unknown operation: {request.Operation}. Supported operations: RunCode, RunFile, RunCSharpCode, RunCSharpFile"
                }
            };
        }

        private async Task<ToolResult> RunCodeAsync(IDictionary<string, object> parameters, string? defaultLanguage = null)
        {
            if (!parameters.TryGetValue("code", out var codeObj) || codeObj is not string code)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'code' parameter." };

            // Determine language
            string language = defaultLanguage ?? "csharp";
            if (parameters.TryGetValue("language", out var langObj) && langObj is string lang)
            {
                language = lang.ToLowerInvariant();
            }

            try
            {
                LogInfo($"Executing {language} code (length: {code.Length})");

                // Get the appropriate language executor
                var executor = _executorFactory.GetExecutor(language);
                if (executor == null)
                {
                    return new ToolResult
                    {
                        IsSuccess = false,
                        Error = $"Unsupported language: {language}"
                    };
                }

                // Execute the code
                var (success, output, error) = await executor.ExecuteCodeAsync(code);
                
                if (!success)
                {
                    LogError($"Execution failed: {error}");
                    return new ToolResult
                    {
                        IsSuccess = false,
                        Error = $"Code execution failed: {error}",
                        Output = output
                    };
                }

                LogInfo($"Code executed successfully. Output: {output}");
                return new ToolResult
                {
                    IsSuccess = true,
                    Output = output
                };
            }
            catch (Exception ex)
            {
                LogError($"Error executing code: {ex.Message}");
                return new ToolResult
                {
                    IsSuccess = false,
                    Error = $"Error executing code: {ex.Message}"
                };
            }
        }

        private async Task<ToolResult> RunFileAsync(IDictionary<string, object> parameters, string? defaultLanguage = null)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'path' parameter." };

            try
            {
                // Since IFileSystem doesn't have FileExistsAsync, we'll use try-catch instead
                string code;
                try
                {
                    code = await _fileSystem.ReadAllTextAsync(path);
                    LogInfo($"Read file {path} (length: {code.Length})");
                }
                catch (FileNotFoundException)
                {
                    LogError($"File not found: {path}");
                    return new ToolResult
                    {
                        IsSuccess = false,
                        Error = $"File not found: {path}"
                    };
                }
                
                // Determine language from file extension if not specified
                string language = defaultLanguage ?? GetLanguageFromFilePath(path);
                if (parameters.TryGetValue("language", out var langObj) && langObj is string lang)
                {
                    language = lang.ToLowerInvariant();
                }

                // Get the appropriate language executor
                var executor = _executorFactory.GetExecutor(language);
                if (executor == null)
                {
                    return new ToolResult
                    {
                        IsSuccess = false,
                        Error = $"Unsupported language: {language}"
                    };
                }

                // Execute the code
                var (success, output, error) = await executor.ExecuteCodeAsync(code);
                
                if (!success)
                {
                    LogError($"Execution failed: {error}");
                    return new ToolResult
                    {
                        IsSuccess = false,
                        Error = $"Code execution failed: {error}",
                        Output = output
                    };
                }

                LogInfo($"Code executed successfully. Output: {output}");
                return new ToolResult
                {
                    IsSuccess = true,
                    Output = output
                };
            }
            catch (Exception ex)
            {
                LogError($"Error executing code from file: {ex.Message}");
                return new ToolResult
                {
                    IsSuccess = false,
                    Error = $"Error executing code from file: {ex.Message}"
                };
            }
        }

        private string GetLanguageFromFilePath(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            
            return extension switch
            {
                ".cs" => "csharp",
                ".py" => "python",
                _ => "unknown"
            };
        }
    }
} 