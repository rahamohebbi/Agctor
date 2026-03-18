using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using System.IO;
using System.Text;
using AgctorSDK.Core.Sessions.Messages;
using AgctorSDK.Core.Sessions.Models;

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
        private string? _sessionId;

        private readonly string _searchAgentId;
        private readonly string _llmAgentId;
        private readonly string _coderAgentId;
        private const string SessionCoordinatorAgentId = "session-coordinator-agent";
        private readonly SemaphoreSlim _requestLock = new(1, 1);
        private readonly Dictionary<string, string> _promptHeaders = new() { ["MessageType"] = "Prompt" };

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
                    await _requestLock.WaitAsync(ct);
                    // Capture routing so we can reply asynchronously
                    _originalSenderId = env.Headers.GetValueOrDefault("SenderId");
                    if (env.Metadata?.TryGetValue("CorrelationId", out var cidObj) == true)
                        _rootCorrelationId = cidObj?.ToString();
                    else if (env.Headers.TryGetValue("CorrelationId", out var cidHdr))
                        _rootCorrelationId = cidHdr;
                    _sessionId = ExtractSessionId(env);

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
                    await Task.Yield(); // Allow processing loop to pick up the message before returning
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
            finally
            {
                _requestLock.Release();
            }
        }

        private async Task<string> ExecuteRefactorAsync(string prompt, CancellationToken ct)
        {
            if (AgentFactory?.RuntimeAdapter == null)
                throw new InvalidOperationException("RuntimeAdapter missing in RefactorAgent");

            // Preserve deterministic behavior when a prebuilt tool command is already provided.
            if (IsCodeEditorCommand(prompt))
            {
                return await ExecuteEditorCommandAsync(prompt, ct);
            }

            // 1. Ask SearchAgent for context (optional but helps the LLM)
            LogInfo("[RefactorAgent] Step 1: requesting context from SearchAgent");
            var context = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                _searchAgentId,
                prompt,
                timeout: TimeSpan.FromSeconds(20),
                senderId: Id,
                headers: _promptHeaders,
                cancellationToken: ct);

            // 1b. Pull session context for follow-up disambiguation and continuity.
            var sessionContext = await TryLoadSessionContextAsync(prompt, ct);

            // Preserve original indexed behavior first; use session-augmented search only when needed.
            if (string.IsNullOrWhiteSpace(context) && !string.IsNullOrWhiteSpace(sessionContext))
            {
                var fallbackPrompt = BuildSearchPrompt(prompt, sessionContext);
                context = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                    _searchAgentId,
                    fallbackPrompt,
                    timeout: TimeSpan.FromSeconds(20),
                    senderId: Id,
                    headers: _promptHeaders,
                    cancellationToken: ct);
            }

            if (string.IsNullOrWhiteSpace(context) && !string.IsNullOrWhiteSpace(sessionContext))
            {
                context = $"No direct indexed match was found for this turn.\nUse the previous chat context below to resolve references.\n\n{sessionContext}";
            }

            // 2. Build LLM prompt – instruct to output JSON with path+code only
            LogInfo("[RefactorAgent] Step 2: sending prompt to LLM agent");

            static string DetectLanguage(string filePath)
            {
                return System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
                {
                    ".cs" => "C#",
                    ".py" => "Python",
                    ".ts" => "TypeScript",
                    ".js" => "JavaScript",
                    ".java" => "Java",
                    _ => "code"
                };
            }

            // Attempts to infer language by scanning the prompt for any filename with a known extension.
            static string DetectLanguageFromPrompt(string text)
            {
                var m = Regex.Match(text, @"\w+\.(cs|py|ts|js|java)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    return DetectLanguage(m.Value);
                }
                return "code";
            }

            var langName = DetectLanguageFromPrompt(prompt);
            LogInfo($"[RefactorAgent] Detected language = {langName}");

            // (Path not yet known – sanitisation after JSON parse)

            // Build LLM prompt with StringBuilder to avoid brace-escaping complications
            var sbPrompt = new StringBuilder();
            sbPrompt.AppendLine($"You are an expert {langName} refactoring assistant.");
            sbPrompt.AppendLine("INSTRUCTIONS:");
            sbPrompt.AppendLine("- Decide whether the REQUEST requires a brand-new file, an insertion, or a patch to existing code.");
            sbPrompt.AppendLine("- Use ONLY CONTEXT and SESSION_CONTEXT for existing-code assumptions; do not invent file/class names.");
            sbPrompt.AppendLine();
            sbPrompt.AppendLine("Return a ONE-LINE JSON object with these keys (omit keys that are not needed):");
            sbPrompt.AppendLine("  operation  : 'WriteFile' | 'InsertIntoFile' | 'ReplaceInFile'");
            sbPrompt.AppendLine("  path       : relative file path (e.g. 'MathUtils.cs')");
            sbPrompt.AppendLine("  content    : the code snippet to write / insert / replace (escape quotes and newlines)");
            sbPrompt.AppendLine("  selector   : semantic selector like 'class:MathUtils' or 'class:MathUtils > method:Cube'");
            sbPrompt.AppendLine("  anchor     : plain text anchor (used only if selector is not provided)");
            sbPrompt.AppendLine("  lineNumber : integer (fallback when both selector and anchor fail)");
            sbPrompt.AppendLine("  startLine  : integer (for ReplaceInFile fallback)");
            sbPrompt.AppendLine("  endLine    : integer (for ReplaceInFile fallback)");
            sbPrompt.AppendLine();
            sbPrompt.AppendLine("Rules:");
            sbPrompt.AppendLine("- For WriteFile, 'content' must be the entire file.");
            sbPrompt.AppendLine("- For InsertIntoFile, give either 'selector', 'anchor', or 'lineNumber'. Prefer 'selector'.");
            sbPrompt.AppendLine("- For ReplaceInFile, prefer 'selector'; otherwise use 'anchor' or the startLine/endLine pair.");
            sbPrompt.AppendLine("- Do NOT wrap the JSON in markdown. NO comments. NO ellipsis.");
            sbPrompt.AppendLine("- If the request is ambiguous, reply with {\"error\":\"reason\"}.");
            sbPrompt.AppendLine();
            sbPrompt.AppendLine("EXAMPLES:");
            sbPrompt.AppendLine("  # Add a method to an existing static class declared inside a namespace");
            sbPrompt.AppendLine("  {\"operation\":\"InsertIntoFile\",\"path\":\"AgctorSDK/Utils/MathUtils.cs\",\"selector\":\"class:MathUtils\",\"content\":\"public static double Division(double a, double b) { return a / b; }\"}");
            sbPrompt.AppendLine();
            sbPrompt.AppendLine("  # Update an existing method");
            sbPrompt.AppendLine("  {\"operation\":\"ReplaceInFile\",\"path\":\"AgctorSDK/Utils/MathUtils.cs\",\"selector\":\"class:MathUtils > method:Square\",\"content\":\"public static int Square(int x) { return x * x; }\"}");
            sbPrompt.AppendLine();
            sbPrompt.AppendLine("  # Create a brand-new file");
            sbPrompt.AppendLine("  {\"operation\":\"WriteFile\",\"path\":\"Foo.cs\",\"content\":\"namespace Demo { public class Foo { } }\"}");
            sbPrompt.AppendLine();
            sbPrompt.AppendLine("  # Remove an existing method");
            sbPrompt.AppendLine("  {\"operation\":\"ReplaceInFile\",\"path\":\"MathUtils.cs\",\"selector\":\"class:MathUtils > method:Division\",\"content\":\"\"}");
            sbPrompt.AppendLine();
            sbPrompt.AppendLine("GUIDELINES:");
            sbPrompt.AppendLine("  - When inserting into a class scope, provide ONLY the new members (methods, properties, fields) without any surrounding namespace or class declarations.");
            sbPrompt.AppendLine();
            sbPrompt.AppendLine("CONTEXT:");
            sbPrompt.AppendLine(context);
            if (!string.IsNullOrWhiteSpace(sessionContext))
            {
                sbPrompt.AppendLine();
                sbPrompt.AppendLine("SESSION_CONTEXT:");
                sbPrompt.AppendLine(sessionContext);
            }
            sbPrompt.AppendLine();
            sbPrompt.AppendLine($"REQUEST: {prompt}");
            sbPrompt.AppendLine("JSON:");

            var llmPrompt = sbPrompt.ToString();

            var llmResponse = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                _llmAgentId,
                llmPrompt,
                timeout: TimeSpan.FromSeconds(180),
                senderId: Id,
                headers: _promptHeaders,
                cancellationToken: ct);

            LogInfo("[RefactorAgent] LLM response received. Parsing …");

            if (IsLlmFailure(llmResponse))
            {
                return "Refactor failed: LLM was unavailable or returned an empty response.";
            }

            // 3. Parse JSON
            string path;
            string code;
            string operation = "WriteFile";
            string? selector = null;
            string? anchor = null;
            int? lineNumber = null;
            int? startLine = null;
            int? endLine = null;
            try
            {
                var normalized = NormalizeJsonCandidate(llmResponse);
                using var doc = JsonDocument.Parse(normalized);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errProp))
                    return $"LLM error: {errProp.GetString()}";

                operation = root.TryGetProperty("operation", out var opProp) ? opProp.GetString() ?? "WriteFile" : "WriteFile";
                path = root.GetProperty("path").GetString() ?? throw new Exception("path missing");
                if (root.TryGetProperty("content", out var contProp))
                {
                    code = contProp.GetString() ?? string.Empty;
                }
                else if (root.TryGetProperty("code", out var codeProp))
                {
                    code = codeProp.GetString() ?? string.Empty;
                }
                else
                {
                    code = string.Empty;
                }

                if (root.TryGetProperty("selector", out var selProp)) selector = selProp.GetString();
                if (root.TryGetProperty("anchor", out var ancProp)) anchor = ancProp.GetString();
                if (root.TryGetProperty("lineNumber", out var lnProp) && lnProp.TryGetInt32(out var lnVal)) lineNumber = lnVal;
                if (root.TryGetProperty("startLine", out var slProp) && slProp.TryGetInt32(out var slVal)) startLine = slVal;
                if (root.TryGetProperty("endLine", out var elProp) && elProp.TryGetInt32(out var elVal)) endLine = elVal;
            }
            catch (Exception)
            {
                // Attempt lenient extraction – tolerate missing escapes or stray characters
                if (!TryExtractPathAndCode(llmResponse, out path, out code))
                {
                    var repaired = await TryRepairJsonAsync(llmResponse, llmPrompt, ct);
                    if (string.IsNullOrWhiteSpace(repaired))
                    {
                        return $"Failed to parse LLM response. Raw: {llmResponse}";
                    }

                    try
                    {
                        using var repairDoc = JsonDocument.Parse(NormalizeJsonCandidate(repaired));
                        var repairRoot = repairDoc.RootElement;
                        if (repairRoot.TryGetProperty("error", out var repairErr))
                        {
                            return $"LLM error: {repairErr.GetString()}";
                        }

                        operation = repairRoot.TryGetProperty("operation", out var repairOpProp) ? repairOpProp.GetString() ?? "WriteFile" : "WriteFile";
                        path = repairRoot.GetProperty("path").GetString() ?? throw new Exception("path missing");
                        if (repairRoot.TryGetProperty("content", out var repairContProp))
                        {
                            code = repairContProp.GetString() ?? string.Empty;
                        }
                        else if (repairRoot.TryGetProperty("code", out var repairCodeProp))
                        {
                            code = repairCodeProp.GetString() ?? string.Empty;
                        }
                        else
                        {
                            code = string.Empty;
                        }

                        if (repairRoot.TryGetProperty("selector", out var repairSelProp)) selector = repairSelProp.GetString();
                        if (repairRoot.TryGetProperty("anchor", out var repairAncProp)) anchor = repairAncProp.GetString();
                        if (repairRoot.TryGetProperty("lineNumber", out var repairLnProp) && repairLnProp.TryGetInt32(out var repairLnVal)) lineNumber = repairLnVal;
                        if (repairRoot.TryGetProperty("startLine", out var repairSlProp) && repairSlProp.TryGetInt32(out var repairSlVal)) startLine = repairSlVal;
                        if (repairRoot.TryGetProperty("endLine", out var repairElProp) && repairElProp.TryGetInt32(out var repairElVal)) endLine = repairElVal;
                    }
                    catch
                    {
                        return $"Failed to parse LLM response. Raw: {llmResponse}";
                    }
                }
            }

            // Sanitize path – remove stray quotes/backticks produced by some LLMs
            path = path.Trim().Trim('\'', '"', '`');

            // If language looked like generic 'code', retry detection using the file path
            if (langName == "code")
            {
                var langFromPath = DetectLanguage(path);
                if (!string.Equals(langFromPath, "code", StringComparison.OrdinalIgnoreCase))
                    langName = langFromPath;
            }

            // If the snippet lacks 'class' or 'namespace' keywords, it probably represents member-only code.
            bool looksLikeMemberSnippet = code.Trim().Length > 0 && !Regex.IsMatch(code, "\\b(class|namespace)\\b", RegexOptions.IgnoreCase);
            if (looksLikeMemberSnippet && (operation == "WriteFile" || operation == "ReplaceInFile"))
            {
                var inferredClass = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(selector))
                    selector = $"class:{inferredClass}";

                operation = "InsertIntoFile";
                LogInfo($"[RefactorAgent] Adjusted operation to InsertIntoFile with selector '{selector}' because snippet lacked class/namespace declarations.");
            }
            else if (operation == "WriteFile")
            {
                // Detect class wrapper without namespace – treat inner body as members
                var inferredClass = Path.GetFileNameWithoutExtension(path);
                var classRegex = new Regex($"class\\s+{inferredClass}\\s*\\{{([\\s\\S]*)\\}}", RegexOptions.IgnoreCase);
                var m = classRegex.Match(code);
                if (m.Success && !Regex.IsMatch(code, "\\bnamespace\\b", RegexOptions.IgnoreCase))
                {
                    var inner = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(inner))
                    {
                        code = inner;
                        selector = $"class:{inferredClass}";
                        operation = "InsertIntoFile";
                        LogInfo($"[RefactorAgent] Extracted members from class wrapper and switched to InsertIntoFile.");
                    }
                }
            }

            // --- NEW LOGGING -------------------------------------------------------
            LogInfo($"[RefactorAgent] LLM output → path='{path}', code length={code.Length} chars");
            var preview = code.Length > 400 ? code.Substring(0, 400) + " …" : code;
            LogInfo($"[RefactorAgent] LLM code preview:\n{preview}");
            // -----------------------------------------------------------------------

            // 4. Build CodeEditorTool command (WriteFile overwrites the file)
            string BuildEditorCommand()
            {
                string esc(string s) => s.Replace("\"", "\\\"").Replace("\n", "\\n");

                switch (operation)
                {
                    case "InsertIntoFile":
                        {
                            var cmd = $"CodeEditorTool InsertIntoFile --path \"{path}\" --content \"{esc(code)}\"";
                            if (!string.IsNullOrEmpty(selector)) cmd += $" --selector \"{esc(selector)}\"";
                            else if (!string.IsNullOrEmpty(anchor)) cmd += $" --anchor \"{esc(anchor)}\"";
                            else if (lineNumber.HasValue) cmd += $" --lineNumber {lineNumber.Value}";
                            return cmd;
                        }
                    case "ReplaceInFile":
                    case "ApplyPatch":
                        {
                            var cmd = $"CodeEditorTool ReplaceInFile --path \"{path}\" --content \"{esc(code)}\"";
                            if (!string.IsNullOrEmpty(selector)) cmd += $" --selector \"{esc(selector)}\"";
                            else if (!string.IsNullOrEmpty(anchor)) cmd += $" --anchor \"{esc(anchor)}\"";
                            else if (startLine.HasValue && endLine.HasValue) cmd += $" --startLine {startLine.Value} --endLine {endLine.Value}";
                            return cmd;
                        }
                    default:
                        {
                            var cmd = $"CodeEditorTool WriteFile --path \"{path}\" --content \"{esc(code)}\"";
                            return cmd;
                        }
                }
            }

            LogInfo($"[RefactorAgent] Building CodeEditorTool command for path '{path}', operation={operation}");
            var editorCmd = BuildEditorCommand();

            var toolResult = await AgentFactory.RuntimeAdapter.SendMessageAsync<AgctorSDK.Core.Tools.Models.ToolResult>(
                _coderAgentId,
                editorCmd,
                timeout: TimeSpan.FromMinutes(8),
                senderId: Id,
                headers: BuildCoderHeaders(),
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
                // Extract path
                var pMatch = Regex.Match(raw, "\\\"path\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                if (!pMatch.Success) return false;
                path = pMatch.Groups[1].Value.Trim();

                // Extract content|code with a single regex allowing either quote or backtick delimiter
                var cMatch = Regex.Match(raw, "\\\"(?:content|code)\\\"\\s*:\\s*([`\"])([\\s\\S]*?)\\1");
                if (!cMatch.Success) return false;

                code = cMatch.Groups[2].Value.Trim();

                // Strip any leading colon/backtick leftovers
                code = code.TrimStart(':', ' ', '`');

                return true;
            }
            catch { return false; }
        }

        private static string? ExtractSessionId(IMessageEnvelope env)
        {
            if (env.Metadata.TryGetValue("sessionId", out var mdVal) && mdVal != null && !string.IsNullOrWhiteSpace(mdVal.ToString()))
            {
                return mdVal.ToString();
            }
            if (env.Metadata.TryGetValue("session-id", out var mdAlt) && mdAlt != null && !string.IsNullOrWhiteSpace(mdAlt.ToString()))
            {
                return mdAlt.ToString();
            }
            if (env.Headers.TryGetValue("session-id", out var headerVal) && !string.IsNullOrWhiteSpace(headerVal))
            {
                return headerVal;
            }
            if (env.Headers.TryGetValue("SessionId", out var headerAlt) && !string.IsNullOrWhiteSpace(headerAlt))
            {
                return headerAlt;
            }
            return null;
        }

        private static string NormalizeJsonCandidate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }

            var text = raw.Trim();
            // Strip fenced markdown payloads (```json ... ``` or ``` ... ```).
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                text = Regex.Replace(text, "^```(?:json)?\\s*", string.Empty, RegexOptions.IgnoreCase);
                text = Regex.Replace(text, "\\s*```$", string.Empty, RegexOptions.IgnoreCase);
                text = text.Trim();
            }

            return text;
        }

        private async Task<string> ExecuteEditorCommandAsync(string editorCommand, CancellationToken ct)
        {
            var toolResult = await AgentFactory!.RuntimeAdapter.SendMessageAsync<AgctorSDK.Core.Tools.Models.ToolResult>(
                _coderAgentId,
                editorCommand,
                timeout: TimeSpan.FromMinutes(8),
                senderId: Id,
                headers: BuildCoderHeaders(),
                cancellationToken: ct);

            return toolResult.IsSuccess
                ? "Command executed and build/tests succeeded."
                : $"Refactor failed: {toolResult.Error}";
        }

        private IDictionary<string, string> BuildCoderHeaders()
        {
            var headers = new Dictionary<string, string>(_promptHeaders);
            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                headers["session-id"] = _sessionId!;
                headers["SessionId"] = _sessionId!;
            }
            return headers;
        }

        private async Task<string> TryLoadSessionContextAsync(string prompt, CancellationToken ct)
        {
            if (AgentFactory?.RuntimeAdapter == null || string.IsNullOrWhiteSpace(_sessionId))
            {
                return string.Empty;
            }

            try
            {
                var package = await AgentFactory.RuntimeAdapter.SendMessageAsync<SessionContextPackage>(
                    SessionCoordinatorAgentId,
                    new GetSessionContextMessage
                    {
                        SessionId = _sessionId,
                        CurrentPrompt = prompt
                    },
                    timeout: TimeSpan.FromSeconds(20),
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "SessionContextRequest" },
                    cancellationToken: ct);
                var context = package.PromptContext ?? string.Empty;
                LogInfo($"[RefactorAgent] Session context loaded for session {_sessionId}; length={context.Length}");
                return context;
            }
            catch (Exception ex)
            {
                LogWarning($"[RefactorAgent] Could not load session context for '{_sessionId}': {ex.Message}");
                return string.Empty;
            }
        }

        private async Task<string?> TryRepairJsonAsync(string rawResponse, string originalPrompt, CancellationToken ct)
        {
            if (AgentFactory?.RuntimeAdapter == null || string.IsNullOrWhiteSpace(rawResponse))
            {
                return null;
            }

            var repairPrompt = $@"You output an invalid or malformed JSON object.
Return ONLY valid one-line JSON with keys:
operation, path, content, selector, anchor, lineNumber, startLine, endLine, error.
Do not include markdown fences or commentary.

ORIGINAL_PROMPT:
{originalPrompt}

MALFORMED_OUTPUT:
{rawResponse}

JSON:";

            try
            {
                var repaired = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                    _llmAgentId,
                    repairPrompt,
                    timeout: TimeSpan.FromSeconds(120),
                    senderId: Id,
                    headers: _promptHeaders,
                    cancellationToken: ct);
                return NormalizeJsonCandidate(repaired);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildSearchPrompt(string prompt, string sessionContext)
        {
            if (string.IsNullOrWhiteSpace(sessionContext))
            {
                return prompt;
            }

            return $@"CURRENT_QUESTION:
{prompt}

RECENT_SESSION_CONTEXT:
{sessionContext}";
        }

        private static bool IsLlmFailure(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
            {
                return true;
            }

            return answer.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCodeEditorCommand(string prompt)
        {
            return !string.IsNullOrWhiteSpace(prompt) &&
                   prompt.TrimStart().StartsWith("CodeEditorTool", StringComparison.OrdinalIgnoreCase);
        }
    }
} 