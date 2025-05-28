using System;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Represents metadata associated with a message envelope.
    /// Contains system-level information about message routing, timing, and actor references.
    /// </summary>
    public interface IMessageMetadata
    {
        /// <summary>
        /// Identifier of the actor that sent this message.
        /// Used for reply routing and audit trails.
        /// </summary>
        string SenderId { get; }

        /// <summary>
        /// Identifier of the target actor that should receive this message.
        /// Used for message routing and delivery.
        /// </summary>
        string ReceiverId { get; }

        /// <summary>
        /// Timestamp when the message was created/sent.
        /// Used for message ordering, timeout handling, and debugging.
        /// </summary>
        DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Optional correlation ID for linking related messages together.
        /// Useful for tracking request-response pairs and conversation flows.
        /// </summary>
        string? CorrelationId { get; }

        /// <summary>
        /// Optional reply-to address for response messages.
        /// Allows for flexible routing patterns and callback mechanisms.
        /// </summary>
        string? ReplyTo { get; }

        /// <summary>
        /// Message priority level for queue ordering and processing.
        /// Higher values indicate higher priority.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Optional expiration time for the message.
        /// Messages past this time should be discarded or handled as expired.
        /// </summary>
        DateTimeOffset? ExpiresAt { get; }

        /// <summary>
        /// The type name of the message payload.
        /// Used for deserialization and message routing based on type.
        /// </summary>
        string MessageType { get; }

        /// <summary>
        /// Version of the message schema/format.
        /// Enables backward compatibility and message evolution.
        /// </summary>
        string Version { get; }
    }
} 