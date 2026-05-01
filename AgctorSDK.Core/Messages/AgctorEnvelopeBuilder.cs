using System;
using System.Collections.Generic;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Messages;

/// <summary>
/// Factory helpers for actor envelopes that carry the standard AGCTOR protocol
/// headers consistently across runtimes and actors.
/// </summary>
public static class AgctorEnvelopeBuilder
{
    public const string ProtocolVersion = "1.0";

    public static MessageEnvelope Command(
        object payload,
        string senderId,
        string receiverId,
        string? messageType = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IDictionary<string, object>? metadata = null)
    {
        return Create(
            payload,
            senderId,
            receiverId,
            messageType ?? InferMessageType(payload, AgctorMessageTypes.Command),
            correlationId,
            headers,
            metadata);
    }

    public static MessageEnvelope Request(
        object payload,
        string senderId,
        string receiverId,
        string correlationId,
        string? messageType = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IDictionary<string, object>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Correlation id is required for request envelopes.", nameof(correlationId));

        return Create(
            payload,
            senderId,
            receiverId,
            messageType ?? InferMessageType(payload, AgctorMessageTypes.Request),
            correlationId,
            headers,
            metadata);
    }

    public static MessageEnvelope Response(
        object payload,
        IMessageEnvelope request,
        string senderId,
        string? messageType = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IDictionary<string, object>? metadata = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var receiverId = request.Headers.TryGetValue(AgctorMessageHeaders.SenderId, out var requester)
            ? requester
            : "unknown";
        var correlationId = request.GetCorrelationId();

        var responseHeaders = MergeHeaders(headers);
        responseHeaders[AgctorMessageHeaders.InReplyTo] = request.Id;

        return Create(
            payload,
            senderId,
            receiverId,
            messageType ?? AgctorMessageTypes.Response,
            correlationId,
            responseHeaders,
            metadata);
    }

    public static MessageEnvelope Acknowledgment(
        IMessageEnvelope request,
        string senderId,
        string? detail = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return Response(
            detail ?? $"Message {request.Id} accepted.",
            request,
            senderId,
            AgctorMessageTypes.Acknowledgment,
            headers: new Dictionary<string, string>
            {
                [AgctorMessageHeaders.ContentType] = "text/plain"
            });
    }

    public static MessageEnvelope Error(
        IMessageEnvelope request,
        string senderId,
        string error,
        Exception? exception = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var metadata = new Dictionary<string, object>
        {
            ["Timestamp"] = DateTimeOffset.UtcNow
        };
        if (exception != null)
        {
            metadata["ExceptionType"] = exception.GetType().Name;
            metadata["Exception"] = exception.ToString();
        }

        return Response(
            error,
            request,
            senderId,
            AgctorMessageTypes.ErrorResponse,
            headers: new Dictionary<string, string>
            {
                [AgctorMessageHeaders.ContentType] = "text/plain",
                [AgctorMessageHeaders.OriginalMessageId] = request.Id
            },
            metadata: metadata);
    }

    private static MessageEnvelope Create(
        object payload,
        string senderId,
        string receiverId,
        string messageType,
        string? correlationId,
        IReadOnlyDictionary<string, string>? headers,
        IDictionary<string, object>? metadata)
    {
        if (string.IsNullOrWhiteSpace(senderId))
            throw new ArgumentException("Sender id is required.", nameof(senderId));
        if (string.IsNullOrWhiteSpace(receiverId))
            throw new ArgumentException("Receiver id is required.", nameof(receiverId));
        if (string.IsNullOrWhiteSpace(messageType))
            throw new ArgumentException("Message type is required.", nameof(messageType));

        var mergedHeaders = MergeHeaders(headers);
        mergedHeaders[AgctorMessageHeaders.SenderId] = senderId;
        mergedHeaders[AgctorMessageHeaders.ReceiverId] = receiverId;
        mergedHeaders[AgctorMessageHeaders.MessageType] = messageType;
        mergedHeaders[AgctorMessageHeaders.Version] = ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            mergedHeaders[AgctorMessageHeaders.CorrelationId] = correlationId;
        }

        var mergedMetadata = metadata != null
            ? new Dictionary<string, object>(metadata)
            : new Dictionary<string, object>();
        if (!mergedMetadata.ContainsKey("Timestamp"))
        {
            mergedMetadata["Timestamp"] = DateTimeOffset.UtcNow;
        }
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            mergedMetadata[AgctorMessageHeaders.CorrelationId] = correlationId;
        }

        return new MessageEnvelope(payload, mergedMetadata, headers: mergedHeaders);
    }

    private static Dictionary<string, string> MergeHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        return headers != null
            ? new Dictionary<string, string>(headers)
            : new Dictionary<string, string>();
    }

    private static string InferMessageType(object payload, string fallback)
    {
        if (payload is string) return AgctorMessageTypes.Prompt;
        return payload?.GetType().Name ?? fallback;
    }
}

