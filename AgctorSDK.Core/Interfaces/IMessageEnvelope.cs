using System.Collections.Generic;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Represents a message envelope that wraps actor messages with metadata, headers, and routing information.
    /// This interface provides a standardized way to handle message transmission between actors across different backends.
    /// </summary>
    public interface IMessageEnvelope
    {
        /// <summary>
        /// Unique identifier for this message envelope.
        /// Used for message tracking, correlation, and deduplication.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The actual message payload/content being transmitted.
        /// Can be any serializable object that represents the actor message.
        /// </summary>
        object Payload { get; }

        /// <summary>
        /// A dictionary of key-value pairs for optional, application-specific, or contextual data.
        /// This can include information such as priority, language, correlationId, timestamp, etc.
        /// Conforms to the Model Context Protocol (MCP).
        /// </summary>
        IDictionary<string, object> Metadata { get; }

        /// <summary>
        /// Custom headers for routing information, message type, agent details, and other protocol-level information.
        /// Keys and values are strings. Conforms to the Model Context Protocol (MCP).
        /// Examples include 'content-type', 'agent', 'reply-to'.
        /// </summary>
        IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>
        /// Creates a new message envelope with the same structure but different payload.
        /// Useful for message transformation and forwarding scenarios.
        /// </summary>
        /// <param name="newPayload">The new payload to wrap in the envelope</param>
        /// <returns>A new message envelope with the updated payload</returns>
        IMessageEnvelope WithPayload(object newPayload);

        /// <summary>
        /// Creates a new message envelope with all existing headers replaced by the provided ones.
        /// </summary>
        /// <param name="replacementHeaders">The complete set of new headers</param>
        /// <returns>A new message envelope with the replaced headers</returns>
        IMessageEnvelope WithHeaders(IDictionary<string, string> replacementHeaders);

        /// <summary>
        /// Creates a new message envelope with a specific header added or updated.
        /// If the key already exists, its value is updated; otherwise, a new header is added.
        /// </summary>
        /// <param name="key">The header key</param>
        /// <param name="value">The header value</param>
        /// <returns>A new message envelope with the updated header</returns>
        IMessageEnvelope WithHeader(string key, string value);

        /// <summary>
        /// Creates a new message envelope with all existing metadata replaced by the provided dictionary.
        /// </summary>
        /// <param name="replacementMetadata">The complete set of new metadata</param>
        /// <returns>A new message envelope with the replaced metadata</returns>
        IMessageEnvelope WithMetadata(IDictionary<string, object> replacementMetadata);

        /// <summary>
        /// Creates a new message envelope with a specific metadata entry added or updated.
        /// If the key already exists, its value is updated; otherwise, a new entry is added.
        /// </summary>
        /// <param name="key">The metadata key</param>
        /// <param name="value">The metadata value</param>
        /// <returns>A new message envelope with the updated metadata entry</returns>
        IMessageEnvelope WithMetadata(string key, object value);
    }
} 