using System;
using System.Threading.Channels;

namespace AgctorSDK.Core.Streaming
{
    /// <summary>
    /// Maps a stream id to a channel so actors (e.g. LLMAgent) can publish deltas while HTTP holds SSE open.
    /// </summary>
    public interface IAgentOutputStreamRegistry
    {
        /// <summary>Registers the writer for a stream id; dispose to unregister (does not complete the writer).</summary>
        IDisposable Register(string streamId, ChannelWriter<AgentStreamEvent> writer);

        void Publish(string streamId, AgentStreamEvent evt);
    }
}
