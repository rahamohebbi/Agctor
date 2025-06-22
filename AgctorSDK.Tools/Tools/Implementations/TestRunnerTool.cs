using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Tools.LanguageTestRunners;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.Implementations
{
    /// <summary>
    /// Tool actor that triggers language-specific test execution.
    /// </summary>
    public class TestRunnerTool : Agent, IToolActor
    {
        private readonly ILanguageTestRunnerFactory _runnerFactory;

        public TestRunnerTool(string id) : this(id, new LanguageTestRunnerFactory())
        {
        }

        public TestRunnerTool(string id, ILanguageTestRunnerFactory? runnerFactory = null) : base(id)
        {
            _runnerFactory = runnerFactory ?? new LanguageTestRunnerFactory();
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is ProcessPromptMessage promptMsg)
            {
                await ProcessPromptAsync(promptMsg.Prompt, cancellationToken);
                return new MessageEnvelope(new ToolResult { IsSuccess = true });
            }
            else if (envelope.Payload is ToolRequest req)
            {
                var res = await Handle(req);
                return new MessageEnvelope(res);
            }

            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            var req = ParsePrompt(prompt);
            if (req.Operation == "Error")
            {
                var err = req.Parameters.TryGetValue("Error", out var e) ? e?.ToString() : "Parse error";
                await FinalizeTaskAsFailed(new Exception(err), cancellationToken);
                return;
            }

            var result = await Handle(req);
            if (result.IsSuccess)
                await FinalizeTask(result, cancellationToken);
            else
                await FinalizeTaskAsFailed(new Exception(result.Error ?? "Test run failed"), cancellationToken);
        }

        public ToolRequest ParsePrompt(string prompt)
        {
            var match = System.Text.RegularExpressions.Regex.Match(prompt, "(TestRunnerTool\\s+\\w+.*)");
            if (!match.Success)
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Could not find TestRunnerTool command" } } };

            var cmd = match.Groups[1].Value.Trim();
            var opMatch = System.Text.RegularExpressions.Regex.Match(cmd, "TestRunnerTool\\s+(\\w+)");
            if (!opMatch.Success)
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { { "Error", "Could not parse operation" } } };

            var operation = opMatch.Groups[1].Value;
            var parameters = ParseParameters(cmd);
            return new ToolRequest { Operation = operation, Parameters = parameters };
        }

        private Dictionary<string, object> ParseParameters(string cmd)
        {
            var dict = new Dictionary<string, object>();
            var pathMatch = System.Text.RegularExpressions.Regex.Match(cmd, "--path\\s+(?:\"([^\"]*)\"|([^\\s]+))");
            if (pathMatch.Success)
            {
                var value = pathMatch.Groups[1].Success ? pathMatch.Groups[1].Value : pathMatch.Groups[2].Value;
                dict["path"] = value;
            }

            var langMatch = System.Text.RegularExpressions.Regex.Match(cmd, "--language\\s+(?:\"([^\"]*)\"|([^\\s]+))");
            if (langMatch.Success)
            {
                var value = langMatch.Groups[1].Success ? langMatch.Groups[1].Value : langMatch.Groups[2].Value;
                dict["language"] = value;
            }

            return dict;
        }

        public async Task<ToolResult> Handle(ToolRequest request)
        {
            return request.Operation switch
            {
                "RunTests" => await RunTestsAsync(request.Parameters),
                _ => new ToolResult { IsSuccess = false, Error = $"Unknown operation: {request.Operation}. Supported: RunTests" }
            };
        }

        private async Task<ToolResult> RunTestsAsync(IDictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("path", out var pathObj) || pathObj is not string path)
                return new ToolResult { IsSuccess = false, Error = "Missing 'path' parameter." };

            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return new ToolResult { IsSuccess = false, Error = $"Path not found: {path}" };
            }

            var language = parameters.TryGetValue("language", out var langObj) && langObj is string langStr ? langStr.ToLowerInvariant() : InferLanguageFromPath(path);

            var runner = _runnerFactory.GetRunner(language);
            if (runner == null)
            {
                return new ToolResult { IsSuccess = false, Error = $"Unsupported language: {language}" };
            }

            var (success, output, error) = await runner.RunTestsAsync(path);
            return new ToolResult { IsSuccess = success, Output = output, Error = error };
        }

        private string InferLanguageFromPath(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".csproj" or ".sln" => "csharp",
                _ => "csharp"
            };
        }
    }
} 