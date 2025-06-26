using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Interfaces;
using System.Collections.Generic;
using System.Linq;

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

        // Holds the original request context so we can reply synchronously
        private string? _originalSenderId;
        private string? _correlationId;
        private TaskCompletionSource<ToolResult>? _responseTcs;

        public CoderAgent(string id) : base(id) { }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            // Capture sender + correlation id for later reply
            if (envelope.Payload is string prompt && envelope.Headers.TryGetValue("MessageType", out var mt) && mt == "Prompt")
            {
                // Attempt to capture SenderId (case-insensitive) and CorrelationId
                _originalSenderId = envelope.Headers.GetValueOrDefault("SenderId")
                                   ?? envelope.Headers.GetValueOrDefault("senderId")
                                   ?? envelope.Headers.GetValueOrDefault("sender-id");

                if (envelope.Metadata != null && envelope.Metadata.TryGetValue("CorrelationId", out var cidObj))
                {
                    _correlationId = cidObj?.ToString();
                }
                else if (envelope.Headers.TryGetValue("CorrelationId", out var cidHdr))
                {
                    _correlationId = cidHdr;
                }

                // Verbose diagnostics
                LogInfo($"Envelope headers: {string.Join(", ", envelope.Headers.Select(kv => $"{kv.Key}={kv.Value}"))}");
                LogInfo($"Envelope metadata: {string.Join(", ", envelope.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))}");

                LogInfo($"[CoderAgent] Prompt received from {_originalSenderId}, correlation={_correlationId}");

                _responseTcs = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                _ = ProcessPromptAsync(prompt, cancellationToken); // fire-and-forget orchestration

                // Wait for orchestration to finish and return final ToolResult
                var finalResult = await _responseTcs.Task.WaitAsync(cancellationToken);

                var respHeaders = new Dictionary<string,string>
                {
                    {"SenderId", Id},
                    {"ReceiverId", _originalSenderId ?? "unknown"},
                    {"MessageType","ToolResult"}
                };
                var respMeta = new Dictionary<string,object>
                {
                    {"CorrelationId", _correlationId ?? string.Empty},
                    {"Timestamp", DateTimeOffset.UtcNow}
                };
                return new MessageEnvelope(finalResult, respMeta, null, respHeaders);
            }

            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LogInfo($"CoderAgent starting orchestration. Initial prompt: {prompt}");
            LogInfo("[CoderAgent] Stage = Edit. Spawning CodeEditorTool …");
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

            // Surface the failure as a ToolResult so the parent awaiting SendMessageAsync<ToolResult>() gets a response.
            var failed = new ToolResult
            {
                IsSuccess = false,
                Error = error.Message,
                Output = _result.TestOutput ?? _result.CompileOutput ?? _result.EditorOutput
            };

            // Send failure result to parent and, if applicable, to the original synchronous caller.
            await FinalizeTask(failed, cancellationToken);
            await SendReplyAsync(failed, cancellationToken);
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
            LogInfo("[CoderAgent] Edit step complete – proceeding to compile");
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
            LogInfo("[CoderAgent] Compile step complete – running tests");
            string testPrompt = "TestRunnerTool RunTests --path \"Agctor.sln\"";
            await AssignSubtaskAsync(testPrompt, "TestRunnerTool", ct);
        }

        private async Task HandleTestCompletionAsync(ToolResult tr, CancellationToken ct)
        {
            _result.TestOutput = tr.Output?.ToString();
            _result.Success = tr.IsSuccess;
            _result.Error = tr.Error;
            _stage = Stage.Done;
            LogInfo("[CoderAgent] Test step finished – finalizing");

            if (tr.IsSuccess)
            {
                await FinalizeTask(_result, ct);
            }
            else
            {
                await FinalizeTaskAsFailed(new Exception(tr.Error ?? "Tests failed"), ct);
            }

            await SendReplyAsync(tr, ct);
        }

        private static string? ExtractPathFromEditorOutput(string? output)
        {
            if (string.IsNullOrWhiteSpace(output)) return null;
            var m = Regex.Match(output, @"File written to\s+(.*)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        protected override async Task FinalizeTask(object result, CancellationToken cancellationToken)
        {
            await base.FinalizeTask(result, cancellationToken);

            if (result is ToolResult tr && _responseTcs != null && !_responseTcs.Task.IsCompleted)
            {
                _responseTcs.TrySetResult(tr);
            }
        }

        protected override async Task FinalizeTaskAsFailed(Exception error, CancellationToken cancellationToken)
        {
            await base.FinalizeTaskAsFailed(error, cancellationToken);

            if (_responseTcs != null && !_responseTcs.Task.IsCompleted)
            {
                _responseTcs.TrySetResult(new ToolResult { IsSuccess = false, Error = error.Message });
            }
        }

        private async Task SendReplyAsync(ToolResult tr, CancellationToken ct)
        {
            // no-op now – synchronous reply is handled via _responseTcs and ReceiveAsync return
            await Task.CompletedTask;
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