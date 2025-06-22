using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Orchestrates source-code modifications (CodeEditorTool) followed by compile & test validation.
    /// The expected prompt is *already* a CodeEditorTool command (produced by upstream LLM or user).
    /// </summary>
    public sealed class CoderAgent : Agent
    {
        private enum Stage { Edit, Compile, Test, Done }
        private Stage _stage = Stage.Edit;
        private string? _changedFilePath;
        private readonly CoderResult _result = new();

        public CoderAgent(string id) : base(id) { }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LogInfo($"CoderAgent starting orchestration. Initial prompt: {prompt}");
            _stage = Stage.Edit;
            await AssignSubtaskAsync(prompt, "CodeEditorTool", cancellationToken);
        }

        public override async Task HandleSubtaskCompletionAsync(string childAgentId, object result, CancellationToken cancellationToken = default)
        {
            if (result is not ToolResult tr)
            {
                await FinalizeTaskAsFailed(new Exception("Unexpected subtask result type"), cancellationToken);
                return;
            }

            switch (_stage)
            {
                case Stage.Edit:
                    await HandleEditCompletionAsync(tr, cancellationToken);
                    break;
                case Stage.Compile:
                    await HandleCompileCompletionAsync(tr, cancellationToken);
                    break;
                case Stage.Test:
                    await HandleTestCompletionAsync(tr, cancellationToken);
                    break;
            }
        }

        public override async Task HandleSubtaskFailureAsync(string childAgentId, Exception error, CancellationToken cancellationToken = default)
        {
            LogError($"Subtask {childAgentId} failed: {error.Message}");
            await FinalizeTaskAsFailed(error, cancellationToken);
        }

        private async Task HandleEditCompletionAsync(ToolResult tr, CancellationToken ct)
        {
            _result.EditorOutput = tr.Output?.ToString();
            if (!tr.IsSuccess)
            {
                await FinalizeTaskAsFailed(new Exception(tr.Error ?? "Edit failed"), ct);
                return;
            }

            _changedFilePath = ExtractPathFromEditorOutput(tr.Output?.ToString());
            if (string.IsNullOrWhiteSpace(_changedFilePath))
            {
                await FinalizeTaskAsFailed(new Exception("Could not determine changed file path."), ct);
                return;
            }

            _stage = Stage.Compile;
            string compilePrompt = $"CompileTool CompileFile --path \"{_changedFilePath}\"";
            await AssignSubtaskAsync(compilePrompt, "CompileTool", ct);
        }

        private async Task HandleCompileCompletionAsync(ToolResult tr, CancellationToken ct)
        {
            _result.CompileOutput = tr.Output?.ToString();
            if (!tr.IsSuccess)
            {
                await FinalizeTaskAsFailed(new Exception(tr.Error ?? "Compilation failed"), ct);
                return;
            }

            _stage = Stage.Test;
            string testPrompt = "TestRunnerTool RunTests --path \"Agctor.sln\"";
            await AssignSubtaskAsync(testPrompt, "TestRunnerTool", ct);
        }

        private async Task HandleTestCompletionAsync(ToolResult tr, CancellationToken ct)
        {
            _result.TestOutput = tr.Output?.ToString();
            _result.Success = tr.IsSuccess;
            _result.Error = tr.Error;
            _stage = Stage.Done;

            if (tr.IsSuccess)
                await FinalizeTask(_result, ct);
            else
                await FinalizeTaskAsFailed(new Exception(tr.Error ?? "Tests failed"), ct);
        }

        private static string? ExtractPathFromEditorOutput(string? output)
        {
            if (string.IsNullOrWhiteSpace(output)) return null;
            var m = Regex.Match(output, @"File written to\s+(.*)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
    }

    /// <summary>
    /// Aggregated result returned by <see cref="CoderAgent"/>.
    /// </summary>
    public class CoderResult
    {
        public bool Success { get; set; }
        public string? EditorOutput { get; set; }
        public string? CompileOutput { get; set; }
        public string? TestOutput { get; set; }
        public string? Error { get; set; }
    }
} 