namespace AgctorSDK.Core.Messages;

/// <summary>
/// Standard actor message header names. New framework code should use these
/// constants instead of repeating string literals across actors and runtimes.
/// </summary>
public static class AgctorMessageHeaders
{
    public const string SenderId = "SenderId";
    public const string ReceiverId = "ReceiverId";
    public const string MessageType = "MessageType";
    public const string MessageId = "MessageId";
    public const string CorrelationId = "CorrelationId";
    public const string ReplyTo = "ReplyTo";
    public const string InReplyTo = "InReplyTo";
    public const string OriginalMessageId = "OriginalMessageId";
    public const string ContentType = "ContentType";
    public const string Version = "Version";
}

