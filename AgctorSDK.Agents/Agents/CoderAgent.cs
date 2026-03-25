using System;
using System.IO;
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
    /// Orchestrates source-code modifications (CodeEditorTool) followed by compile & test validation for <c>.cs</c> edits.
    /// Non-C# files (e.g. <c>.md</c>) skip compile/test so markdown/docs are not fed to Roslyn.
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
        private string? _embeddingCoordinatorAgentId;
        private readonly SemaphoreSlim _requestLock = new(1, 1);
        private bool _requestLockHeld;

        public CoderAgent(string id) : base(id) { }

        public void ConfigureEmbeddingCoordinator(string? embeddingCoordinatorAgentId)
        {
            _embeddingCoordinatorAgentId = embeddingCoordinatorAgentId;
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            // Passthrough: self-sent result so ReplyProxy completes the pending HTTP request
            if (envelope.Headers.TryGetValue("MessageType", out var msgType) &&
                (msgType == "ToolResult" || msgType == "Result" || msgType == "Error"))
            {
                return envelope;
            }

            // Capture sender + correlation id for later reply
            if (envelope.Payload is string prompt && envelope.Headers.TryGetValue("MessageType", out var mt) && mt == "Prompt")
            {
                await _requestLock.WaitAsync(cancellationToken);
                _requestLockHeld = true;
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

                if (!IsCodeEditorCommand(prompt))
                {
                    var guidance = new ToolResult
                    {
                        IsSuccess = false,
                        Error = "coder-agent expects a CodeEditorTool command. For natural-language requests and follow-ups, use refactor-agent so session context and LLM planning can resolve intent safely."
                    };
                    _ = SendReplyAsync(guidance, cancellationToken);

                    var guidanceAckHeaders = new Dictionary<string, string>
                    {
                        { "SenderId", Id },
                        { "ReceiverId", _originalSenderId ?? "unknown" },
                        { "MessageType", "Acknowledgment" }
                    };
                    var guidanceAckMeta = new Dictionary<string, object>
                    {
                        { "Timestamp", DateTimeOffset.UtcNow }
                    };
                    return new MessageEnvelope("Started", guidanceAckMeta, null, guidanceAckHeaders);
                }

                _ = ProcessPromptAsync(prompt, cancellationToken); // fire-and-forget orchestration

                // Return immediate ACK so this ReceiveAsync completes and actor can process further messages
                var ackHeaders = new Dictionary<string,string>
                {
                    {"SenderId", Id},
                    {"ReceiverId", _originalSenderId ?? "unknown"},
                    {"MessageType","Acknowledgment"}
                };
                var ackMeta = new Dictionary<string,object>
                {
                    {"Timestamp", DateTimeOffset.UtcNow}
                };
                return new MessageEnvelope("Started", ackMeta, null, ackHeaders);
            }

            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            // Reset child tracking so previous runs don't count towards limit
            ClearChildAgents();
            _stage = Stage.Edit;

            LogInfo($"CoderAgent starting orchestration. Initial prompt: {prompt}");
            LogInfo("[CoderAgent] Stage = Edit. Spawning CodeEditorTool …");
            await AssignSubtaskAsync(prompt, "CodeEditorTool", cancellationToken);
        }

        public override async Task HandleSubtaskCompletionAsync(string childAgentId, object result, CancellationToken cancellationToken = default)
        {
            if (result is not ToolResult tr)
            {
                var err = new ToolResult { IsSuccess = false, Error = "Unexpected subtask result type" };
                await FinalizeTaskAsFailed(new Exception(err.Error), cancellationToken);
                await SendReplyAsync(err, cancellationToken);
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

            var failed = new ToolResult
            {
                IsSuccess = false,
                Error = error.Message,
                Output = _result.TestOutput ?? _result.CompileOutput ?? _result.EditorOutput
            };

            await FinalizeTask(failed, cancellationToken);
            await SendReplyAsync(failed, cancellationToken);
        }

        private async Task HandleEditCompletionAsync(ToolResult tr, CancellationToken ct)
        {
            _result.EditorOutput = tr.Output?.ToString();
            if (!tr.IsSuccess)
            {
                await FinalizeTaskAsFailed(new Exception(tr.Error ?? "Edit failed"), ct);
                await SendReplyAsync(tr, ct);
                return;
            }

            _changedFilePath = ExtractPathFromEditorOutput(tr.Output?.ToString());
            if (string.IsNullOrWhiteSpace(_changedFilePath))
            {
                var err = new ToolResult { IsSuccess = false, Error = "Could not determine changed file path." };
                await FinalizeTaskAsFailed(new Exception(err.Error), ct);
                await SendReplyAsync(err, ct);
                return;
            }

            if (!RequiresCSharpCompileStep(_changedFilePath))
            {
                // Markdown, JSON, etc. are not valid C# — compiling them produces huge Roslyn noise.
                _stage = Stage.Done;
                _result.Success = true;
                _result.EditorOutput = tr.Output?.ToString();
                LogInfo($"[CoderAgent] Edit complete for non-C# file '{_changedFilePath}' — skipping compile/test.");
                await MarkEmbeddingsStaleAsync(ct);
                var ok = new ToolResult
                {
                    IsSuccess = true,
                    Output = $"File written to {_changedFilePath} (documentation/non-C# file: compile and tests skipped)."
                };
                await FinalizeTask(_result, ct);
                await SendReplyAsync(ok, ct);
                return;
            }

            _stage = Stage.Compile;
            LogInfo("[CoderAgent] Edit step complete – proceeding to compile");
            string compilePrompt = $"CompileTool CompileFile --path \"{_changedFilePath}\"";
            await AssignSubtaskAsync(compilePrompt, "CompileTool", ct);
        }

        /// <summary>
        /// Only <c>.cs</c> sources participate in the demo compile/test gate; other extensions are written as-is.
        /// </summary>
        private static bool RequiresCSharpCompileStep(string path) =>
            string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);

        private async Task HandleCompileCompletionAsync(ToolResult tr, CancellationToken ct)
        {
            _result.CompileOutput = tr.Output?.ToString();
            if (!tr.IsSuccess)
            {
                await FinalizeTaskAsFailed(new Exception(tr.Error ?? "Compilation failed"), ct);
                await SendReplyAsync(tr, ct);
                return;
            }

            _stage = Stage.Test;
            LogInfo("[CoderAgent] Compile step complete – running unit tests");
            var testProjRel = Path.Combine("Tests", "AgctorSDK.Core.Tests.csproj");
            string testPrompt = $"TestRunnerTool RunTests --path \"{testProjRel}\"";
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
                await MarkEmbeddingsStaleAsync(ct);
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

        /// <summary>
        /// Sends the final result back so the HTTP client receives it.
        /// Uses same pattern as RefactorAgent: send to self with correlation ID so ReplyProxy completes the pending request.
        /// </summary>
        private async Task SendReplyAsync(ToolResult tr, CancellationToken ct)
        {
            if (_correlationId == null || AgentFactory?.RuntimeAdapter == null)
            {
                LogWarning("Cannot send reply – missing correlation or runtime adapter");
                ReleaseRequestLockIfHeld();
                return;
            }

            try
            {
                var meta = new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTimeOffset.UtcNow,
                    ["CorrelationId"] = _correlationId
                };
                var headers = new Dictionary<string, string>
                {
                    ["SenderId"] = Id,
                    ["ReceiverId"] = Id,
                    ["MessageType"] = "ToolResult"
                };
                var envelope = new MessageEnvelope(tr, meta, null, headers);
                await AgentFactory.RuntimeAdapter.SendMessageAsync(Id, envelope, Id, null, ct);
                LogInfo($"[CoderAgent] Reply enqueued for correlation {_correlationId} Success={tr.IsSuccess}");
            }
            finally
            {
                ReleaseRequestLockIfHeld();
            }
        }

        private async Task MarkEmbeddingsStaleAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_embeddingCoordinatorAgentId) || AgentFactory?.RuntimeAdapter == null)
            {
                return;
            }

            try
            {
                await AgentFactory.RuntimeAdapter.SendMessageAsync(
                    _embeddingCoordinatorAgentId,
                    "mark embeddings stale",
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" },
                    cancellationToken: cancellationToken);
                LogInfo($"[CoderAgent] Marked embeddings stale via {_embeddingCoordinatorAgentId}");
            }
            catch (Exception ex)
            {
                LogWarning($"[CoderAgent] Failed to mark embeddings stale: {ex.Message}");
            }
        }

        private void ReleaseRequestLockIfHeld()
        {
            if (_requestLockHeld)
            {
                _requestLockHeld = false;
                _requestLock.Release();
            }
        }

        private static bool IsCodeEditorCommand(string prompt)
        {
            return !string.IsNullOrWhiteSpace(prompt) &&
                   prompt.TrimStart().StartsWith("CodeEditorTool", StringComparison.OrdinalIgnoreCase);
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