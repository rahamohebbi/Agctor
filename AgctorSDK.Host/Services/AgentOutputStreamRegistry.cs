using System.Collections.Concurrent;
using System.Threading.Channels;
using AgctorSDK.Core.Streaming;

namespace AgctorSDK.Host.Services
{
    /// <summary>
    /// In-process registry of active SSE streams (PRD-011).
    /// </summary>
    public sealed class AgentOutputStreamRegistry : IAgentOutputStreamRegistry
    {
        private readonly ConcurrentDictionary<string, ChannelWriter<AgentStreamEvent>> _writers = new(StringComparer.Ordinal);

        public IDisposable Register(string streamId, ChannelWriter<AgentStreamEvent> writer)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            _writers[streamId] = writer;
            return new Registration(this, streamId);
        }

        public void Publish(string streamId, AgentStreamEvent evt)
        {
            if (string.IsNullOrWhiteSpace(streamId) || evt == null)
            {
                return;
            }

            if (_writers.TryGetValue(streamId, out var w))
            {
                w.TryWrite(evt);
            }
        }

        private void Unregister(string streamId) => _writers.TryRemove(streamId, out _);

        private sealed class Registration : IDisposable
        {
            private readonly AgentOutputStreamRegistry _owner;
            private readonly string _streamId;
            private int _disposed;

            public Registration(AgentOutputStreamRegistry owner, string streamId)
            {
                _owner = owner;
                _streamId = streamId;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1)
                {
                    return;
                }

                _owner.Unregister(_streamId);
            }
        }
    }
}
