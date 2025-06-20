using System;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.CodeGraph.Messages;
using System.Collections.Generic;

namespace AgctorSDK.CodeGraph.Intents
{
    /// <summary>
    /// Intent resolver that forwards the prompt to an existing <see cref="Agents.IntentDetectionAgent"/> via the actor runtime.
    /// </summary>
    public sealed class ProxyIntentResolver : IIntentResolver
    {
        private readonly IActorRuntimeAdapter _runtime;
        private readonly string _intentAgentId;
        private readonly TimeSpan _timeout;

        public ProxyIntentResolver(IActorRuntimeAdapter runtime, string intentAgentId, TimeSpan? timeout = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _intentAgentId = intentAgentId ?? throw new ArgumentNullException(nameof(intentAgentId));
            _timeout = timeout ?? TimeSpan.FromSeconds(10);
        }

        public IntentResolution Resolve(string prompt)
        {
            var msg = new InterpretQueryMessage(prompt);
            try
            {
                var task = _runtime.SendMessageAsync<IntentResolvedMessage>(_intentAgentId, msg, _timeout);
                task.Wait();
                var resp = task.Result;
                return resp?.Resolution ?? IntentResolution.Unresolved;
            }
            catch
            {
                return IntentResolution.Unresolved;
            }
        }
    }
} 