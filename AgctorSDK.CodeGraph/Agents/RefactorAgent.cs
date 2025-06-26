using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.CodeGraph.Agents
{
    /// <summary>
    /// Orchestrates a refactor request: gathers context, asks the LLM for an explicit file rewrite,
    /// then delegates the edit to CoderAgent (which validates with compile/tests).
    /// </summary>
    public sealed class RefactorAgent : Agent
    {
        // Track original caller for async reply
        private string? _originalSenderId;
        private string? _rootCorrelationId;

        private readonly string _searchAgentId;
        private readonly string _llmAgentId;
        private readonly string _coderAgentId;

        public RefactorAgent(string id, string searchAgentId, string llmAgentId, string coderAgentId) : base(id)
        {
            _searchAgentId = searchAgentId ?? throw new ArgumentNullException(nameof(searchAgentId));
            _llmAgentId    = llmAgentId    ?? throw new ArgumentNullException(nameof(llmAgentId));
            _coderAgentId  = coderAgentId  ?? throw new ArgumentNullException(nameof(coderAgentId));
        }

        protected override async Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken)
        {
            var result = await ExecuteRefactorAsync(prompt, cancellationToken);
            await FinalizeTask(result, cancellationToken);
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope env, CancellationToken ct = default)
        {
            if (env.Headers.TryGetValue("MessageType", out var mt))
            {
                // --- PROMPT (entry point) -----------------------------------------------------
                if (mt == "Prompt" && env.Payload is string prompt)
                {
                    // Capture routing so we can reply asynchronously
                    _originalSenderId = env.Headers.GetValueOrDefault("SenderId");
                    if (env.Metadata?.TryGetValue("CorrelationId", out var cidObj) == true)
                        _rootCorrelationId = cidObj?.ToString();
                    else if (env.Headers.TryGetValue("CorrelationId", out var cidHdr))
                        _rootCorrelationId = cidHdr;

                    // Kick off orchestration without blocking the actor message loop
                    _ = Task.Run(() => OrchestrateRefactorAsync(prompt, ct), ct);

                    // Immediate ACK so runtime keeps processing
                    var ackHeaders = new Dictionary<string,string>
                    {
                        ["SenderId"]   = Id,
                        ["ReceiverId"] = _originalSenderId ?? "unknown",
                        ["MessageType"] = "Acknowledgment"
                    };
                    var ackMeta = new Dictionary<string,object> { ["Timestamp"] = DateTimeOffset.UtcNow };
                    return new MessageEnvelope("Started", ackMeta, null, ackHeaders);
                }

                // --- TOOL RESULT or FINAL RESULT passthrough ---------------------------------
                if ((mt == "ToolResult" && env.Payload is AgctorSDK.Core.Tools.Models.ToolResult) || mt == "Result" || mt == "Error")
                {
                    // Simply echo so runtime resolves the pending correlation.
                    return env;
                }
            }

            return await base.ReceiveAsync(env, ct);
        }

        private async Task OrchestrateRefactorAsync(string prompt, CancellationToken ct)
        {
            try
            {
                var resultString = await ExecuteRefactorAsync(prompt, ct);

                // Send final reply back to original caller (HTTP) using captured correlation
                if (_rootCorrelationId != null && AgentFactory?.RuntimeAdapter != null)
                {
                    // Send the result back to *this* agent with the correlation ID so the runtime
                    // completes the pending request created by MessageDispatcher.
                    var meta = new Dictionary<string,object>
                    {
                        ["Timestamp"] = DateTimeOffset.UtcNow,
                        ["CorrelationId"] = _rootCorrelationId
                    };
                    var hdr = new Dictionary<string,string>
                    {
                        ["SenderId"]   = Id,
                        ["ReceiverId"] = Id,
                        ["MessageType"] = "Result"
                    };
                    var envelope = new MessageEnvelope(resultString, meta, null, hdr);
                    await AgentFactory.RuntimeAdapter.SendMessageAsync(Id, envelope, Id, null, ct);
                    LogInfo($"[RefactorAgent] Final result enqueued for correlation {_rootCorrelationId}");
                }
            }
            catch (Exception ex)
            {
                LogError($"[RefactorAgent] Orchestration failed: {ex.Message}");
                if (_rootCorrelationId != null && AgentFactory?.RuntimeAdapter != null)
                {
                    var meta = new Dictionary<string,object>
                    {
                        ["Timestamp"] = DateTimeOffset.UtcNow,
                        ["CorrelationId"] = _rootCorrelationId
                    };
                    var hdr = new Dictionary<string,string>
                    {
                        ["SenderId"]   = Id,
                        ["ReceiverId"] = Id,
                        ["MessageType"] = "Error"
                    };
                    var envelope = new MessageEnvelope($"Error: {ex.Message}", meta, null, hdr);
                    await AgentFactory.RuntimeAdapter.SendMessageAsync(Id, envelope, Id, null, ct);
                }
            }
        }

        private async Task<string> ExecuteRefactorAsync(string prompt, CancellationToken ct)
        {
            if (AgentFactory?.RuntimeAdapter == null)
                throw new InvalidOperationException("RuntimeAdapter missing in RefactorAgent");

            // 1. Ask SearchAgent for context (optional but helps the LLM)
            LogInfo("[RefactorAgent] Step 1: requesting context from SearchAgent");
            var context = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                _searchAgentId,
                prompt,
                timeout: TimeSpan.FromSeconds(20),
                senderId: Id,
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" },
                cancellationToken: ct);

            // 2. Build LLM prompt – instruct to output JSON with path+code only
            LogInfo("[RefactorAgent] Step 2: sending prompt to LLM agent");
            var llmPrompt = @$"You are an expert C# refactoring assistant.
INSTRUCTIONS:
- Given the CONTEXT and the REQUEST, output a single-line JSON object with fields 'path' and 'code'.
- 'path' is the relative file path to modify (e.g. 'MathUtils.cs').
- 'code' is the COMPLETE revised contents of that file. Do NOT wrap the JSON in markdown.
- Do NOT include any extra keys or comments.
- If insufficient information, reply with {{""error"":""reason""}}.

CONTEXT:
{context}

REQUEST: {prompt}
JSON:";

            var llmResponse = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                _llmAgentId,
                llmPrompt,
                timeout: TimeSpan.FromSeconds(180),
                senderId: Id,
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" },
                cancellationToken: ct);

            LogInfo("[RefactorAgent] LLM response received. Parsing …");

            // 3. Parse JSON
            string path;
            string code;
            try
            {
                using var doc = JsonDocument.Parse(llmResponse);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errProp))
                    return $"LLM error: {errProp.GetString()}";

                path = root.GetProperty("path").GetString() ?? throw new Exception("path missing");
                code = root.GetProperty("code").GetString() ?? throw new Exception("code missing");
            }
            catch (Exception)
            {
                // Attempt lenient extraction – tolerate missing escapes or stray characters
                if (!TryExtractPathAndCode(llmResponse, out path, out code))
                    return $"Failed to parse LLM response. Raw: {llmResponse}";
            }

            // 4. Build CodeEditorTool command (WriteFile overwrites the file)
            LogInfo($"[RefactorAgent] Building CodeEditorTool command for path '{path}'");
            var escaped = code.Replace("\"", "\\\"").Replace("\n", "\\n");
            var editorCmd = $"CodeEditorTool WriteFile --path \"{path}\" --content \"{escaped}\"";

            var toolResult = await AgentFactory.RuntimeAdapter.SendMessageAsync<AgctorSDK.Core.Tools.Models.ToolResult>(
                _coderAgentId,
                editorCmd,
                timeout: TimeSpan.FromMinutes(8),
                senderId: Id,
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" },
                cancellationToken: ct);

            LogInfo($"[RefactorAgent] ToolResult received – success: {toolResult.IsSuccess}");

            return toolResult.IsSuccess
                ? $"File {path} updated and build/tests {(toolResult.IsSuccess ? "succeeded" : "failed")}."
                : $"Refactor failed: {toolResult.Error}";
        }

        protected override bool ShouldDecomposeTask(string prompt) => false;

        static bool TryExtractPathAndCode(string raw, out string path, out string code)
        {
            path = code = string.Empty;
            try
            {
                var pMatch = Regex.Match(raw, "\\\"path\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                if (!pMatch.Success) return false;
                path = pMatch.Groups[1].Value.Trim();

                var cIdx = raw.IndexOf("\"code\"", StringComparison.OrdinalIgnoreCase);
                if (cIdx < 0) return false;
                var firstQuote = raw.IndexOf('"', cIdx + 6);
                if (firstQuote < 0) return false;

                // Code value may span until the last quote before the final }
                var lastQuote = raw.LastIndexOf('"');
                if (lastQuote <= firstQuote) return false;
                code = raw.Substring(firstQuote + 1, lastQuote - firstQuote - 1);

                // Remove leading characters like + or whitespace
                code = code.TrimStart('+', '\n', '\r', ' ');
                return true;
            }
            catch { return false; }
        }
    }
} 