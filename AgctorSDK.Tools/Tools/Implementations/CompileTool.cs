using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Build;
using AgctorSDK.Core.Tools.LanguageCompilers;
using AgctorSDK.Core.Tools.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.Implementations
{
    /// <summary>
    /// Tool actor that can compile code or source files for multiple languages using the <see cref="ILanguageCompilerFactory"/> abstraction.
    /// </summary>
    public class CompileTool : Agent, IToolActor
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILanguageCompilerFactory _compilerFactory;

        public CompileTool(string id) : this(id, new DefaultFileSystem(), new LanguageCompilerFactory())
        {
        }

        public CompileTool(string id, IFileSystem? fileSystem = null, ILanguageCompilerFactory? compilerFactory = null) : base(id)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystem();
            _compilerFactory = compilerFactory ?? new LanguageCompilerFactory();
        }

        #region IMessage handling

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
            LogInfo($"CompileTool processing prompt: {prompt}");

            try
            {
                var toolRequest = ParsePrompt(prompt);
                if (toolRequest.Operation == "Error")
                {
                    var error = toolRequest.Parameters.TryGetValue("Error", out var e) ? e : "Unknown parse error";
                    await FinalizeTaskAsFailed(new Exception(error?.ToString()), cancellationToken);
                    return;
                }

                var result = await Handle(toolRequest);
                if (result.IsSuccess)
                {
                    await FinalizeTask(result, cancellationToken);
                }
                else
                {
                    await FinalizeTaskAsFailed(new Exception(result.Error ?? "Compilation failed"), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await FinalizeTaskAsFailed(ex, cancellationToken);
            }
        }

        #endregion

        #region Request parsing helpers

        public ToolRequest ParsePrompt(string prompt)
        {
            // We expect a command line that starts with "CompileTool <Operation> ..."
            var match = System.Text.RegularExpressions.Regex.Match(prompt, "(CompileTool\\s+\\w+.*)");
            if (!match.Success)
            {
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Could not find CompileTool command line" } } };
            }

            string commandLine = match.Groups[1].Value.Trim();

            // Extract operation
            var opMatch = System.Text.RegularExpressions.Regex.Match(commandLine, "CompileTool\\s+(\\w+)");
            if (!opMatch.Success)
            {
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Could not parse operation" } } };
            }
            string operation = opMatch.Groups[1].Value;

            var parameters = ParseParameters(commandLine);

            return new ToolRequest { Operation = operation, Parameters = parameters };
        }

        private Dictionary<string, object> ParseParameters(string commandLine)
        {
            var parameters = new Dictionary<string, object>();

            // --path "file"
            var pathMatch = System.Text.RegularExpressions.Regex.Match(commandLine, "--path\\s+(?:\"([^\"]*)\"|([^\\s]+))");
            if (pathMatch.Success)
            {
                var value = pathMatch.Groups[1].Success ? pathMatch.Groups[1].Value : pathMatch.Groups[2].Value;
                parameters["path"] = value;
            }

            // --code "source ..."
            var codeMatch = System.Text.RegularExpressions.Regex.Match(commandLine, "--code\\s+(?:\"([^\"]*)\"|([^\\s]+))");
            if (codeMatch.Success)
            {
                var value = codeMatch.Groups[1].Success ? codeMatch.Groups[1].Value : codeMatch.Groups[2].Value;
                value = value.Replace("\\\"", "\"");
                parameters["code"] = value;
            }

            // --language "csharp" etc.
            var langMatch = System.Text.RegularExpressions.Regex.Match(commandLine, "--language\\s+(?:\"([^\"]*)\"|([^\\s]+))");
            if (langMatch.Success)
            {
                var value = langMatch.Groups[1].Success ? langMatch.Groups[1].Value : langMatch.Groups[2].Value;
                parameters["language"] = value;
            }

            return parameters;
        }

        #endregion

        #region Core handler

        public async Task<ToolResult> Handle(ToolRequest request)
        {
            return request.Operation switch
            {
                "CompileCode" => await CompileCodeAsync(request.Parameters),
                "CompileFile" => await CompileFileAsync(request.Parameters),
                _ => new ToolResult { IsSuccess = false, Error = $"Unknown operation: {request.Operation}. Supported operations: CompileCode, CompileFile" }
            };
        }

        private async Task<ToolResult> CompileCodeAsync(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("code", out var codeObj) || codeObj is not string code)
            {
                return new ToolResult { IsSuccess = false, Error = "Missing 'code' parameter." };
            }

            var language = parameters.TryGetValue("language", out var langObj) && langObj is string langStr ? langStr.ToLowerInvariant() : "csharp";

            var compiler = _compilerFactory.GetCompiler(language);
            if (compiler == null)
            {
                return new ToolResult { IsSuccess = false, Error = $"Unsupported language: {language}" };
            }

            var (success, output, error) = await compiler.CompileCodeAsync(code);
            return new ToolResult { IsSuccess = success, Output = output, Error = error };
        }

        private async Task<ToolResult> CompileFileAsync(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
            {
                return new ToolResult { IsSuccess = false, Error = "Missing 'path' parameter." };
            }

            if (!System.IO.File.Exists(path))
            {
                return new ToolResult { IsSuccess = false, Error = $"File not found: {path}" };
            }

            // Infer language from extension if not provided
            string language = "";
            if (parameters.TryGetValue("language", out var langObj) && langObj is string langStr)
            {
                language = langStr.ToLowerInvariant();
            }
            else
            {
                language = GetLanguageFromFilePath(path);
            }

            var compiler = _compilerFactory.GetCompiler(language);
            if (compiler == null)
            {
                return new ToolResult { IsSuccess = false, Error = $"Unsupported language: {language}" };
            }

            // C# on disk: prefer dotnet build (restore + project refs + tests layout) when a solution/project exists nearby.
            if (string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase) &&
                compiler is CSharpCompiler csharpCompiler)
            {
                var fullPath = Path.GetFullPath(path);
                if (DotNetWorkspaceBuild.IsDotNetCliAvailable())
                {
                    var entry = DotNetWorkspaceBuild.FindSolutionOrProject(fullPath);
                    if (entry != null)
                    {
                        var (ok, outText, errText) = await DotNetWorkspaceBuild.BuildAsync(entry).ConfigureAwait(false);
                        return new ToolResult
                        {
                            IsSuccess = ok,
                            Output = outText,
                            Error = ok ? string.Empty : errText
                        };
                    }
                }

                var (success, output, error) = await csharpCompiler.CompileSameDirectoryWorkspaceAsync(path).ConfigureAwait(false);
                return new ToolResult { IsSuccess = success, Output = output, Error = error };
            }

            string code = await _fileSystem.ReadAllTextAsync(path);
            var (success2, output2, error2) = await compiler.CompileCodeAsync(code).ConfigureAwait(false);
            return new ToolResult { IsSuccess = success2, Output = output2, Error = error2 };
        }

        private string GetLanguageFromFilePath(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".cs" => "csharp",
                ".py" => "python",
                _ => "csharp" // Fallback
            };
        }

        #endregion
    }
} 