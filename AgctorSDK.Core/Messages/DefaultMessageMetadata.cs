using System;
using AgctorSDK.Core.Interfaces; // For IMessageMetadata

namespace AgctorSDK.Core.Messages
{
    /// <summary>
    /// A default, concrete implementation of IMessageMetadata.
    /// </summary>
    public class DefaultMessageMetadata : IMessageMetadata
    {
        public string SenderId { get; } 
        public string ReceiverId { get; } 
        public string ReplyTo { get; } 
        public DateTimeOffset Timestamp { get; } 
        public DateTimeOffset? ExpiresAt { get; } 
        public string CorrelationId { get; } 
        public int Priority { get; } 
        public string MessageType { get; }
        public string Version { get; }

        public DefaultMessageMetadata(string senderId, string receiverId, string? correlationId = null, string? replyTo = null, string messageType = "Default", int priority = 0, string version = "1.0", DateTimeOffset? expiresAt = null) 
        {
            SenderId = senderId ?? throw new ArgumentNullException(nameof(senderId));
            ReceiverId = receiverId ?? throw new ArgumentNullException(nameof(receiverId));
            ReplyTo = replyTo ?? string.Empty;
            Timestamp = DateTimeOffset.UtcNow;
            CorrelationId = correlationId ?? Guid.NewGuid().ToString();
            MessageType = messageType;
            Version = version;
            Priority = priority;
            ExpiresAt = expiresAt;
        }
    }
} 