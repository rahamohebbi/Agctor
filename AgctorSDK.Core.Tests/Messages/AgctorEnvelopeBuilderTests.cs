using AgctorSDK.Core.Messages;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Messages;

public sealed class AgctorEnvelopeBuilderTests
{
    [Fact]
    public void Request_Adds_Standard_Routing_And_Correlation()
    {
        var envelope = AgctorEnvelopeBuilder.Request(
            payload: new TestPayload("hello"),
            senderId: "sender",
            receiverId: "receiver",
            correlationId: "corr-1");

        envelope.Headers[AgctorMessageHeaders.SenderId].Should().Be("sender");
        envelope.Headers[AgctorMessageHeaders.ReceiverId].Should().Be("receiver");
        envelope.Headers[AgctorMessageHeaders.MessageType].Should().Be(nameof(TestPayload));
        envelope.Headers[AgctorMessageHeaders.Version].Should().Be(AgctorEnvelopeBuilder.ProtocolVersion);
        envelope.Headers[AgctorMessageHeaders.CorrelationId].Should().Be("corr-1");
        envelope.Metadata[AgctorMessageHeaders.CorrelationId].Should().Be("corr-1");
        envelope.Metadata.Should().ContainKey("Timestamp");
    }

    [Fact]
    public void Response_Preserves_Request_Correlation()
    {
        var request = AgctorEnvelopeBuilder.Request(
            payload: "question",
            senderId: "caller",
            receiverId: "worker",
            correlationId: "corr-2");

        var response = AgctorEnvelopeBuilder.Response(
            payload: "answer",
            request: request,
            senderId: "worker",
            messageType: AgctorMessageTypes.Result);

        response.Headers[AgctorMessageHeaders.SenderId].Should().Be("worker");
        response.Headers[AgctorMessageHeaders.ReceiverId].Should().Be("caller");
        response.Headers[AgctorMessageHeaders.MessageType].Should().Be(AgctorMessageTypes.Result);
        response.Headers[AgctorMessageHeaders.InReplyTo].Should().Be(request.Id);
        response.GetCorrelationId().Should().Be("corr-2");
    }

    [Fact]
    public void Acknowledgment_Uses_Interim_Message_Type()
    {
        var request = AgctorEnvelopeBuilder.Request("work", "caller", "worker", "corr-3");

        var ack = AgctorEnvelopeBuilder.Acknowledgment(request, "worker");

        ack.GetMessageType().Should().Be(AgctorMessageTypes.Acknowledgment);
        ack.GetCorrelationId().Should().Be("corr-3");
        ack.Headers[AgctorMessageHeaders.ContentType].Should().Be("text/plain");
    }

    [Fact]
    public void Error_Response_Preserves_Correlation_And_Original_Message()
    {
        var request = AgctorEnvelopeBuilder.Request("work", "caller", "worker", "corr-4");

        var error = AgctorEnvelopeBuilder.Error(request, "worker", "failed");

        error.GetMessageType().Should().Be(AgctorMessageTypes.ErrorResponse);
        error.GetCorrelationId().Should().Be("corr-4");
        error.Headers[AgctorMessageHeaders.OriginalMessageId].Should().Be(request.Id);
        error.Payload.Should().Be("failed");
    }

    [Fact]
    public void Command_Adds_Standard_Headers_Even_When_Typo_Variants_Are_Present()
    {
        var envelope = AgctorEnvelopeBuilder.Command(
            payload: "hello",
            senderId: "sender",
            receiverId: "receiver",
            headers: new Dictionary<string, string>
            {
                ["SenderID"] = "wrong-key",
                ["MesssageType"] = "wrong-type"
            });

        envelope.Headers[AgctorMessageHeaders.SenderId].Should().Be("sender");
        envelope.Headers[AgctorMessageHeaders.ReceiverId].Should().Be("receiver");
        envelope.Headers[AgctorMessageHeaders.MessageType].Should().Be(AgctorMessageTypes.Prompt);
        envelope.Headers[AgctorMessageHeaders.Version].Should().Be(AgctorEnvelopeBuilder.ProtocolVersion);
    }

    [Fact]
    public void Correlation_Reader_Does_Not_Use_Misspelled_Header_Key()
    {
        var envelope = new MessageEnvelope(
            payload: "hello",
            metadata: new Dictionary<string, object>(),
            id: "msg-1",
            headers: new Dictionary<string, string>
            {
                ["CorelationId"] = "typo-value"
            });

        envelope.GetCorrelationId().Should().BeNull();
    }

    [Fact]
    public void Message_Type_Reader_Does_Not_Use_Misspelled_Header_Key()
    {
        var envelope = new MessageEnvelope(
            payload: "hello",
            metadata: new Dictionary<string, object>(),
            id: "msg-2",
            headers: new Dictionary<string, string>
            {
                ["MessageTyp"] = "typo-value"
            });

        envelope.GetMessageType().Should().BeNull();
    }

    private sealed record TestPayload(string Value);
}

