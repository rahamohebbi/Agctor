using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using System.Collections.Generic;
using AgctorSDK.Core.Interfaces;
using System.Text.RegularExpressions;

namespace AgctorSDK.CodeGraph.Agents
{
    /// <summary>
    /// Orchestrator that combines SearchAgent and LLMAgent: retrieves relevant code context
    /// then asks the LLM to formulate a final natural-language answer.
    /// </summary>
    public sealed class QueryAgent : Agent
    {
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
            var answer = await ExecuteQueryAsync(prompt, cancellationToken);
            await FinalizeTask(answer, cancellationToken);
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Headers.TryGetValue("MessageType", out var mt) && mt == "Prompt" && envelope.Payload is string prompt)
            {
                string result;
                try
                {
                    result = await ExecuteQueryAsync(prompt, cancellationToken);
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

        private async Task<string> ExecuteQueryAsync(string prompt, CancellationToken cancellationToken)
        {
            if (AgentFactory?.RuntimeAdapter == null)
            {
                throw new InvalidOperationException("RuntimeAdapter not available in QueryAgent");
            }

            var promptHeaders = new Dictionary<string, string> { ["MessageType"] = "Prompt" };

            // 1. Search for relevant context
            var context = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                _searchAgentId,
                prompt,
                timeout: TimeSpan.FromSeconds(15),
                senderId: Id,
                headers: promptHeaders,
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(context))
            {
                return BuildNoContextMessage(prompt);
            }

            // short-circuit: if context already contains the direct answer for a purely structural query
            if (!string.IsNullOrWhiteSpace(context) && IsDirectAnswerPrompt(prompt))
                return context;

            // 2. Build LLM prompt (explicitly forbid hallucination)
            var llmPrompt = $@"You are an expert code assistant.
CONTEXT already is the answer; reformat it and do not invent new code.
If CONTEXT is empty reply with 'No code found for the query'.

---
CONTEXT:
{context}
---
QUESTION: {prompt}
ANSWER:";

            static bool IsDirectAnswerPrompt(string p)
            {
                // Only treat very explicit listing queries as direct-answerable.
                // Exclude generic words like "method" that appear in many refactor prompts.
                return System.Text.RegularExpressions.Regex.IsMatch(
                    p,
                    @"\b(list|show|lines? of code|classes?)\b",
                    RegexOptions.IgnoreCase);
            }

            // 3. Ask LLM
            var answer = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                _llmAgentId,
                llmPrompt,
                timeout: TimeSpan.FromSeconds(170),
                senderId: Id,
                headers: promptHeaders,
                cancellationToken: cancellationToken);

            return answer;
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

        protected override bool ShouldDecomposeTask(string prompt) => false; // orchestrator just forwards
    }
} 