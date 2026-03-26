using System;
using System.Threading.Channels;

namespace AgctorSDK.Core.Streaming
{
    /// <summary>
    /// No-op registry for hosts/tests that do not wire streaming.
    /// </summary>
    public sealed class NullAgentOutputStreamRegistry : IAgentOutputStreamRegistry
    {
        public static readonly NullAgentOutputStreamRegistry Instance = new();

        private NullAgentOutputStreamRegistry()
        {
        }

        public IDisposable Register(string streamId, ChannelWriter<AgentStreamEvent> writer) => NullDisposable.Instance;

        public void Publish(string streamId, AgentStreamEvent evt)
        {
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
