using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using System.Collections.Generic;
using AgctorSDK.Core.Interfaces;

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

            // 2. Build LLM prompt.
            var llmPrompt = $"### Context\n{context}\n\n### Question\n{prompt}\n\n### Answer:";

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

        protected override bool ShouldDecomposeTask(string prompt) => false; // orchestrator just forwards
    }
} 