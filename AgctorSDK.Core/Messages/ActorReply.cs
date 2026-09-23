using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Messages
{
    /// <summary>
    /// Builds the envelope an actor returns from <c>ReceiveAsync</c>.
    /// The in-memory runtime matches request-response calls using the inbound correlation id,
    /// so the reply only needs a typed payload.
    /// </summary>
    public static class ActorReply
    {
        public static MessageEnvelope With(object payload)
        {
            return new MessageEnvelope(
                payload,
                new Dictionary<string, object> { ["Timestamp"] = DateTimeOffset.UtcNow });
        }
    }
}
