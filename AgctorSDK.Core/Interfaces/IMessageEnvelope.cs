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
        /// Message metadata containing system-level information about the message.
        /// Includes routing info, timestamps, sender/receiver details, etc.
        /// </summary>
        IMessageMetadata Metadata { get; }

        /// <summary>
        /// Custom headers for application-specific message properties.
        /// Allows for extensible message attributes without modifying the core envelope structure.
        /// </summary>
        IReadOnlyDictionary<string, object> Headers { get; }

        /// <summary>
        /// Creates a new message envelope with the same structure but different payload.
        /// Useful for message transformation and forwarding scenarios.
        /// </summary>
        /// <param name="newPayload">The new payload to wrap in the envelope</param>
        /// <returns>A new message envelope with the updated payload</returns>
        IMessageEnvelope WithPayload(object newPayload);

        /// <summary>
        /// Creates a new message envelope with additional or updated headers.
        /// Preserves existing headers and adds/overwrites with the provided ones.
        /// </summary>
        /// <param name="additionalHeaders">Headers to add or update</param>
        /// <returns>A new message envelope with updated headers</returns>
        IMessageEnvelope WithHeaders(IDictionary<string, object> additionalHeaders);
    }
} 