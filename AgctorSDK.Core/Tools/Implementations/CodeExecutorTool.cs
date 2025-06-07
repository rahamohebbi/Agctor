using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.Implementations
{
    public class CodeExecutorTool : Agent, IToolActor
    {
        private readonly IFileSystem _fileSystem;

        public CodeExecutorTool(string id) : this(id, new DefaultFileSystem())
        {
        }

        public CodeExecutorTool(string id, IFileSystem? fileSystem = null) : base(id)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystem();
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
            LogInfo($"CodeExecutorTool processing prompt: {prompt}");

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
            var commandLineMatch = System.Text.RegularExpressions.Regex.Match(prompt, @"(CodeExecutorTool\s+\w+.*)");
            if (!commandLineMatch.Success)
            {
                LogWarning($"Could not find command line in input: {prompt}");
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Could not find command line in input" } } };
            }

            string commandLine = commandLineMatch.Groups[1].Value.Trim();
            LogInfo($"Found command line: {commandLine}");

            // Now parse the command line
            var match = System.Text.RegularExpressions.Regex.Match(commandLine, @"CodeExecutorTool\s+(\w+)(.*)");
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

            return parameters;
        }

        public async Task<ToolResult> Handle(ToolRequest request)
        {
            return request.Operation switch
            {
                "RunCSharpCode" => await RunCSharpCodeAsync(request.Parameters),
                "RunCSharpFile" => await RunCSharpFileAsync(request.Parameters),
                _ => new ToolResult
                {
                    IsSuccess = false,
                    Error = $"Unknown operation: {request.Operation}. Supported operations: RunCSharpCode, RunCSharpFile"
                }
            };
        }

        private async Task<ToolResult> RunCSharpCodeAsync(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("code", out var codeObj) || codeObj is not string code)
                return new ToolResult { IsSuccess = false, Error = "Missing or invalid 'code' parameter." };

            try
            {
                LogInfo($"Compiling and executing C# code (length: {code.Length})");
                var (success, output, error) = await CompileAndExecuteCodeAsync(code);
                
                if (!success)
                {
                    LogError($"Compilation or execution failed: {error}");
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

        private async Task<ToolResult> RunCSharpFileAsync(IDictionary<string, object> parameters)
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
                
                var (success, output, error) = await CompileAndExecuteCodeAsync(code);
                
                if (!success)
                {
                    LogError($"Compilation or execution failed: {error}");
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

        private async Task<(bool Success, string Output, string Error)> CompileAndExecuteCodeAsync(string code)
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            try
            {
                // Create compilation
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                
                // Add all necessary references for a basic console application
                var references = new List<MetadataReference>
                {
                    // Basic references
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                    
                    // Additional references for .NET Core/.NET 5+
                    MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
                    MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard").Location),
                    MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections").Location)
                };
                
                // Try to add additional helpful references
                try { references.Add(MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Console").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Text").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.IO").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Threading").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Threading.Tasks").Location)); } catch { }

                var compilation = CSharpCompilation.Create(
                    $"DynamicAssembly_{Guid.NewGuid():N}",
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                );

                // Compile in memory
                using var ms = new MemoryStream();
                var result = compilation.Emit(ms);

                if (!result.Success)
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        errorBuilder.AppendLine(diagnostic.ToString());
                    }
                    return (false, string.Empty, errorBuilder.ToString());
                }

                // Prepare to execute
                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());
                
                // Redirect stdout
                var originalOut = Console.Out;
                using var sw = new StringWriter();
                Console.SetOut(sw);

                try
                {
                    // Find entry point and invoke
                    var entryPoint = assembly.EntryPoint;
                    if (entryPoint == null)
                    {
                        return (false, string.Empty, "No entry point found in the code.");
                    }

                    await Task.Run(() => entryPoint.Invoke(null, new object[] { Array.Empty<string>() }));
                    outputBuilder.Append(sw.ToString());
                }
                finally
                {
                    // Restore stdout
                    Console.SetOut(originalOut);
                }

                return (true, outputBuilder.ToString(), string.Empty);
            }
            catch (Exception ex)
            {
                return (false, outputBuilder.ToString(), $"Execution error: {ex.Message}");
            }
        }
    }
} 