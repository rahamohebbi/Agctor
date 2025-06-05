using System;
using System.Collections.Generic;
using AgctorSDK.Core.Interfaces; // For IMessageEnvelope, IMessageMetadata

namespace AgctorSDK.Core.Messages
{
    /// <summary>
    /// Basic implementation of IMessageEnvelope.
    /// Represents a generic message container used for actor communication.
    /// </summary>
    public class MessageEnvelope : IMessageEnvelope
    {
        public string Id { get; private set; }
        public object Payload { get; private set; }
        // Metadata is now non-nullable to match IMessageEnvelope interface
        public IMessageMetadata Metadata { get; private set; } 
        public IReadOnlyDictionary<string, object> Headers { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageEnvelope"/> class.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <param name="metadata">Message metadata. Cannot be null.</param>
        /// <param name="id">Optional message ID. If null, a new GUID is generated.</param>
        /// <param name="headers">Optional message headers.</param>
        public MessageEnvelope(object payload, IMessageMetadata metadata, string? id = null, IReadOnlyDictionary<string, object>? headers = null)
        {
            Id = id ?? Guid.NewGuid().ToString();
            Payload = payload;
            // Ensure metadata is not null
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata)); 
            Headers = headers ?? new Dictionary<string, object>();
        }
        
        // Overload for convenience if ID is specified before metadata, keeping metadata required.
        public MessageEnvelope(object payload, string id, IMessageMetadata metadata, IReadOnlyDictionary<string, object>? headers = null) 
            : this(payload, metadata, id, headers) { }

        public IMessageEnvelope WithPayload(object newPayload)
        {
            return new MessageEnvelope(newPayload, Metadata, Id, Headers);
        }

        public IMessageEnvelope WithHeaders(IDictionary<string, object> additionalHeaders)
        {
            var newHeaders = new Dictionary<string, object>(Headers);
            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    newHeaders[header.Key] = header.Value;
                }
            }
            return new MessageEnvelope(Payload, Metadata, Id, newHeaders);
        }
    }
} 