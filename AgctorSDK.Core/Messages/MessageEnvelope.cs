using System;
using System.Collections.Generic;
using System.Linq; // Added for defensive copying if needed, and potential future use
using AgctorSDK.Core.Interfaces;
using System.Collections.ObjectModel; // Added for ReadOnlyDictionary

namespace AgctorSDK.Core.Messages
{
    /// <summary>
    /// Basic implementation of <see cref="IMessageEnvelope"/>.
    /// Represents a generic message container used for actor communication, compliant with MCP.
    /// </summary>
    public class MessageEnvelope : IMessageEnvelope
    {
        /// <inheritdoc/>
        public string Id { get; private set; }
        
        /// <inheritdoc/>
        public object Payload { get; private set; }
        
        /// <inheritdoc/>
        public IDictionary<string, object> Metadata { get; private set; }
        
        // Internal backing field for headers
        private readonly Dictionary<string, string> _internalHeaders;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> Headers => new ReadOnlyDictionary<string, string>(_internalHeaders);

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageEnvelope"/> class.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <param name="metadata">Optional message metadata. If null, an empty dictionary is used.</param>
        /// <param name="id">Optional message ID. If null or empty, a new GUID is generated.</param>
        /// <param name="headers">Optional message headers. If null, an empty dictionary is used.</param>
        public MessageEnvelope(
            object payload, 
            IDictionary<string, object>? metadata = null, 
            string? id = null, 
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id;
            Payload = payload;
            Metadata = metadata != null ? new Dictionary<string, object>(metadata) : new Dictionary<string, object>();
            // Initialize internal backing field for headers, creating a new mutable dictionary internally
            _internalHeaders = headers != null ? new Dictionary<string, string>(headers) : new Dictionary<string, string>();
        }

        // Private constructor for internal use by With... methods to efficiently pass internal collections
        private MessageEnvelope(string id, object payload, IDictionary<string, object> metadata, Dictionary<string, string> internalHeaders)
        {
            Id = id;
            Payload = payload;
            Metadata = metadata; // Assumed to be a new instance already by With... methods
            _internalHeaders = internalHeaders; // Assumed to be a new instance already by With... methods
        }

        /// <inheritdoc/>
        public IMessageEnvelope WithPayload(object newPayload)
        {
            // Use the private constructor, passing a new copy of Metadata and _internalHeaders to maintain immutability of the new instance's collections
            // with respect to the original instance, though the dictionaries themselves remain mutable if the caller holds a reference to them.
            return new MessageEnvelope(this.Id, newPayload, new Dictionary<string, object>(this.Metadata), new Dictionary<string, string>(this._internalHeaders));
        }

        /// <inheritdoc/>
        public IMessageEnvelope WithHeaders(IDictionary<string, string> replacementHeaders)
        {
            var newInternalHeaders = replacementHeaders != null ? new Dictionary<string, string>(replacementHeaders) : new Dictionary<string, string>();
            return new MessageEnvelope(this.Id, this.Payload, new Dictionary<string, object>(this.Metadata), newInternalHeaders);
        }

        /// <inheritdoc/>
        public IMessageEnvelope WithHeader(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            
            var newInternalHeaders = new Dictionary<string, string>(this._internalHeaders);
            newInternalHeaders[key] = value;
            return new MessageEnvelope(this.Id, this.Payload, new Dictionary<string, object>(this.Metadata), newInternalHeaders);
        }

        /// <inheritdoc/>
        public IMessageEnvelope WithMetadata(IDictionary<string, object> replacementMetadata)
        {
            var newMetadata = replacementMetadata != null ? new Dictionary<string, object>(replacementMetadata) : new Dictionary<string, object>();
            return new MessageEnvelope(this.Id, this.Payload, newMetadata, new Dictionary<string, string>(this._internalHeaders));
        }

        /// <inheritdoc/>
        public IMessageEnvelope WithMetadata(string key, object value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var newMetadata = new Dictionary<string, object>(this.Metadata);
            newMetadata[key] = value;
            return new MessageEnvelope(this.Id, this.Payload, newMetadata, new Dictionary<string, string>(this._internalHeaders));
        }
    }
} 