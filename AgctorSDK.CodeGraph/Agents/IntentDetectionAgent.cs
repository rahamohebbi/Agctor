using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Intents;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.CodeGraph.Agents
{
    /// <summary>
    /// Actor that delegates prompt understanding to an LLM via <see cref="ILlmClient"/> and returns a structured <see cref="IntentResolution"/>.
    /// Keeps LLM latency outside the orchestrator.
    /// </summary>
    public sealed class IntentDetectionAgent : Agent
    {
        private readonly LlmIntentResolver _resolver;

        public IntentDetectionAgent(string id, ILlmClient client) : base(id)
        {
            _resolver = new LlmIntentResolver(client);
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is InterpretQueryMessage msg)
            {
                var res = await Task.Run(() => _resolver.Resolve(msg.Prompt), cancellationToken);
                return new MessageEnvelope(new IntentResolvedMessage(res), null, id: null,
                    headers: new System.Collections.Generic.Dictionary<string,string>{{"MessageType", "IntentResolved"}});
            }

            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        protected override Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken) => Task.CompletedTask;
    }
} 