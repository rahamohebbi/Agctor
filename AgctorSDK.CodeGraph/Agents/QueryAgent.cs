using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using System.Collections.Generic;
using AgctorSDK.Core.Interfaces;
using System.Text.RegularExpressions;
using AgctorSDK.Core.Sessions.Messages;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.CodeGraph.Agents
{
    /// <summary>
    /// Orchestrator that combines SearchAgent and LLMAgent: retrieves relevant code context
    /// then asks the LLM to formulate a final natural-language answer.
    /// </summary>
    public sealed class QueryAgent : Agent
    {
        private const string SessionCoordinatorAgentId = "session-coordinator-agent";
        private string _searchAgentId = string.Empty;
        private string _llmAgentId = string.Empty;

        public QueryAgent(string id, string searchAgentId, string llmAgentId) : base(id)
        {
            _searchAgentId = searchAgentId ?? throw new ArgumentNullException(nameof(searchAgentId));
            _llmAgentId = llmAgentId ?? throw new ArgumentNullException(nameof(llmAgentId));
        }

        public QueryAgent() { }

        public void Configure(string searchAgentId, string llmAgentId)
        {
            _searchAgentId = searchAgentId ?? throw new ArgumentNullException(nameof(searchAgentId));
            _llmAgentId = llmAgentId ?? throw new ArgumentNullException(nameof(llmAgentId));
        }

        protected override async Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken)
        {
            var answer = await ExecuteQueryAsync(prompt, sessionId: null, cancellationToken);
            await FinalizeTask(answer, cancellationToken);
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Headers.TryGetValue("MessageType", out var mt) && mt == "Prompt" && envelope.Payload is string prompt)
            {
                string result;
                try
                {
                    var sessionId = ExtractSessionId(envelope);
                    result = await ExecuteQueryAsync(prompt, sessionId, cancellationToken);
                }
                catch (Exception ex)
                {
                    result = $"Error: {ex.Message}";
                }

                var headers = new Dictionary<string, string>
                {
                    ["SenderId"] = Id,
                    ["ReceiverId"] = envelope.Headers.GetValueOrDefault("SenderId", "unknown"),
                    ["MessageType"] = "Answer"
                };

                return new MessageEnvelope(result, null, Guid.NewGuid().ToString(), headers);
            }

            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        private async Task<string> ExecuteQueryAsync(string prompt, string? sessionId, CancellationToken cancellationToken)
        {
            if (AgentFactory?.RuntimeAdapter == null)
            {
                throw new InvalidOperationException("RuntimeAdapter not available in QueryAgent");
            }

            var promptHeaders = new Dictionary<string, string> { ["MessageType"] = "Prompt" };
            var sessionContext = await TryLoadSessionContextAsync(sessionId, prompt, cancellationToken);

            // 1. Search for relevant context
            var context = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                _searchAgentId,
                prompt,
                timeout: TimeSpan.FromSeconds(15),
                senderId: Id,
                headers: promptHeaders,
                cancellationToken: cancellationToken);

            // Preserve original indexed behavior first; only use a session-augmented search as fallback.
            if (string.IsNullOrWhiteSpace(context) && !string.IsNullOrWhiteSpace(sessionContext))
            {
                var fallbackPrompt = BuildSearchPrompt(prompt, sessionContext);
                context = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                    _searchAgentId,
                    fallbackPrompt,
                    timeout: TimeSpan.FromSeconds(15),
                    senderId: Id,
                    headers: promptHeaders,
                    cancellationToken: cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(context) && string.IsNullOrWhiteSpace(sessionContext))
            {
                return BuildNoContextMessage(prompt);
            }

            // If semantic search does not find code, preserve continuity with prior session turns.
            if (string.IsNullOrWhiteSpace(context) && !string.IsNullOrWhiteSpace(sessionContext))
            {
                context = $"No direct indexed match was found for this turn.\nUse the previous chat context below to answer carefully.\n\n{sessionContext}";
            }

            // 2. Build LLM prompt (explicitly forbid hallucination)
            var llmPrompt = $@"You are an expert code assistant.
Answer ONLY using CONTEXT and SESSION_CONTEXT. Do not invent facts.
If a question asks about a specific class (for example method count), only use evidence that clearly belongs to that class.
If context is insufficient, say exactly what is missing.
Keep answer concise and deterministic.

---
CONTEXT:
{context}
---
SESSION_CONTEXT:
{sessionContext}
---
QUESTION: {prompt}
ANSWER:";

            // 3. Ask LLM
            var answer = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                _llmAgentId,
                llmPrompt,
                timeout: TimeSpan.FromSeconds(170),
                senderId: Id,
                headers: promptHeaders,
                cancellationToken: cancellationToken);

            if (!IsLlmFailure(answer))
            {
                return answer;
            }

            // Deterministic backup path only when LLM is unavailable/failed.
            var backup = await TryDeterministicBackupAsync(prompt, promptHeaders, cancellationToken);
            return string.IsNullOrWhiteSpace(backup) ? answer : backup;
        }

        private async Task<string> TryLoadSessionContextAsync(string? sessionId, string prompt, CancellationToken cancellationToken)
        {
            if (AgentFactory?.RuntimeAdapter == null || string.IsNullOrWhiteSpace(sessionId))
            {
                return string.Empty;
            }

            try
            {
                var package = await AgentFactory.RuntimeAdapter.SendMessageAsync<SessionContextPackage>(
                    SessionCoordinatorAgentId,
                    new GetSessionContextMessage
                    {
                        SessionId = sessionId,
                        CurrentPrompt = prompt
                    },
                    timeout: TimeSpan.FromSeconds(20),
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "SessionContextRequest" },
                    cancellationToken: cancellationToken);
                return package.PromptContext ?? string.Empty;
            }
            catch
            {
                return string.Empty;
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

        private static bool TryExtractMethodCountClass(string prompt, out string className)
        {
            className = string.Empty;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return false;
            }

            // Examples:
            // - "how many methods does MathUtils have?"
            // - "double check how many methods does MathUtils have?"
            var match = Regex.Match(
                prompt,
                @"\bhow\s+many\s+methods?\s+does\s+([A-Za-z_][A-Za-z0-9_]*)\s+have\b",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return false;
            }

            className = match.Groups[1].Value;
            return !string.IsNullOrWhiteSpace(className);
        }

        private static bool IsLlmFailure(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
            {
                return true;
            }

            return answer.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> TryDeterministicBackupAsync(
            string prompt,
            IDictionary<string, string> promptHeaders,
            CancellationToken cancellationToken)
        {
            if (AgentFactory?.RuntimeAdapter == null)
            {
                return null;
            }

            if (TryExtractMethodCountClass(prompt, out var className))
            {
                var scopedPrompt = $"list methods in class {className}";
                var scopedResult = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                    _searchAgentId,
                    scopedPrompt,
                    timeout: TimeSpan.FromSeconds(15),
                    senderId: Id,
                    headers: promptHeaders,
                    cancellationToken: cancellationToken);

                var scopedMethods = ParseMethodNames(scopedResult);
                if (scopedMethods.Count > 0)
                {
                    return $"`{className}` has {scopedMethods.Count} method(s): {string.Join(", ", scopedMethods)}.";
                }

                if (!string.IsNullOrWhiteSpace(scopedResult) &&
                    scopedResult.Contains("has no methods", StringComparison.OrdinalIgnoreCase))
                {
                    return $"`{className}` has 0 methods.";
                }
            }

            return null;
        }

        private static List<string> ParseMethodNames(string context)
        {
            if (string.IsNullOrWhiteSpace(context))
            {
                return new List<string>();
            }

            var lines = context
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var methods = lines
                .Where(x => Regex.IsMatch(x, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (methods.Count > 0)
            {
                return methods;
            }

            methods = lines
                .Where(x => x.StartsWith("•", StringComparison.Ordinal))
                .Select(x => x.TrimStart('•').Trim())
                .Where(x => Regex.IsMatch(x, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return methods;
        }

        private static string BuildNoContextMessage(string prompt)
        {
            if (IsCodeChangePrompt(prompt))
            {
                return "query-agent answers questions about existing indexed code and cannot create, edit, or delete files. Use coder-agent for code changes or refactor-agent for refactors.";
            }

            return "query-agent could not find matching indexed code for that question. Click Index now and ask about existing code, classes, methods, or files.";
        }

        private static bool IsCodeChangePrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return false;
            }

            // Treat create/edit/refactor wording as a code-change request so the user gets a routing hint.
            return Regex.IsMatch(
                prompt,
                @"\b(create|add|write|implement|modify|edit|update|delete|remove|rename|refactor)\b",
                RegexOptions.IgnoreCase);
        }

        private static string? ExtractSessionId(IMessageEnvelope envelope)
        {
            if (envelope.Metadata.TryGetValue("sessionId", out var sessionObj) &&
                sessionObj != null &&
                !string.IsNullOrWhiteSpace(sessionObj.ToString()))
            {
                return sessionObj.ToString();
            }

            if (envelope.Metadata.TryGetValue("session-id", out var sessionObjAlt) &&
                sessionObjAlt != null &&
                !string.IsNullOrWhiteSpace(sessionObjAlt.ToString()))
            {
                return sessionObjAlt.ToString();
            }

            if (envelope.Headers.TryGetValue("session-id", out var headerSession) &&
                !string.IsNullOrWhiteSpace(headerSession))
            {
                return headerSession;
            }

            if (envelope.Headers.TryGetValue("SessionId", out var headerSessionAlt) &&
                !string.IsNullOrWhiteSpace(headerSessionAlt))
            {
                return headerSessionAlt;
            }

            return null;
        }

        protected override bool ShouldDecomposeTask(string prompt) => false; // orchestrator just forwards
    }
} 