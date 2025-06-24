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
            if (env.Headers.TryGetValue("MessageType", out var mt) && mt == "Prompt" && env.Payload is string prompt)
            {
                var res = await ExecuteRefactorAsync(prompt, ct);
                return new MessageEnvelope(res);
            }
            return await base.ReceiveAsync(env, ct);
        }

        private async Task<string> ExecuteRefactorAsync(string prompt, CancellationToken ct)
        {
            if (AgentFactory?.RuntimeAdapter == null)
                throw new InvalidOperationException("RuntimeAdapter missing in RefactorAgent");

            // 1. Ask SearchAgent for context (optional but helps the LLM)
            var context = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                _searchAgentId,
                prompt,
                timeout: TimeSpan.FromSeconds(20),
                senderId: Id,
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" },
                cancellationToken: ct);

            // 2. Build LLM prompt – instruct to output JSON with path+code only
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
            var escaped = code.Replace("\"", "\\\"").Replace("\n", "\\n");
            var editorCmd = $"CodeEditorTool WriteFile --path \"{path}\" --content \"{escaped}\"";

            var toolResult = await AgentFactory.RuntimeAdapter.SendMessageAsync<AgctorSDK.Core.Tools.Models.ToolResult>(
                _coderAgentId,
                editorCmd,
                timeout: TimeSpan.FromMinutes(3),
                senderId: Id,
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" },
                cancellationToken: ct);

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