using System;
using AgctorSDK.Core.Interfaces;
using Xunit;

namespace AgctorSDK.Core.Tests.Interfaces
{
    /// <summary>
    /// Unit tests for the IMessageMetadata interface contract and behavior.
    /// Tests verify that message metadata implementations properly handle routing, timing, and actor information.
    /// </summary>
    public class IMessageMetadataTests
    {
        /// <summary>
        /// Test implementation of IMessageMetadata for testing purposes.
        /// Provides a concrete implementation with controllable behavior for testing.
        /// </summary>
        private class TestMessageMetadata : IMessageMetadata
        {
            public string SenderId { get; }
            public string ReceiverId { get; }
            public DateTimeOffset Timestamp { get; }
            public string? CorrelationId { get; }
            public string? ReplyTo { get; }
            public int Priority { get; }
            public DateTimeOffset? ExpiresAt { get; }
            public string MessageType { get; }
            public string Version { get; }

            public TestMessageMetadata(
                string senderId,
                string receiverId,
                DateTimeOffset timestamp,
                string messageType,
                string version,
                string? correlationId = null,
                string? replyTo = null,
                int priority = 0,
                DateTimeOffset? expiresAt = null)
            {
                SenderId = senderId;
                ReceiverId = receiverId;
                Timestamp = timestamp;
                CorrelationId = correlationId;
                ReplyTo = replyTo;
                Priority = priority;
                ExpiresAt = expiresAt;
                MessageType = messageType;
                Version = version;
            }
        }

        [Fact]
        public void MessageMetadata_ShouldHaveRequiredProperties()
        {
            // Arrange
            var senderId = "sender-123";
            var receiverId = "receiver-456";
            var timestamp = DateTimeOffset.UtcNow;
            var messageType = "TestMessage";
            var version = "1.0";
            var correlationId = "corr-789";
            var replyTo = "reply-actor";
            var priority = 5;
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

            // Act
            var metadata = new TestMessageMetadata(
                senderId, receiverId, timestamp, messageType, version,
                correlationId, replyTo, priority, expiresAt);

            // Assert
            Assert.Equal(senderId, metadata.SenderId);
            Assert.Equal(receiverId, metadata.ReceiverId);
            Assert.Equal(timestamp, metadata.Timestamp);
            Assert.Equal(correlationId, metadata.CorrelationId);
            Assert.Equal(replyTo, metadata.ReplyTo);
            Assert.Equal(priority, metadata.Priority);
            Assert.Equal(expiresAt, metadata.ExpiresAt);
            Assert.Equal(messageType, metadata.MessageType);
            Assert.Equal(version, metadata.Version);
        }

        [Fact]
        public void MessageMetadata_ShouldHandleNullOptionalProperties()
        {
            // Arrange
            var senderId = "sender-123";
            var receiverId = "receiver-456";
            var timestamp = DateTimeOffset.UtcNow;
            var messageType = "TestMessage";
            var version = "1.0";

            // Act
            var metadata = new TestMessageMetadata(senderId, receiverId, timestamp, messageType, version);

            // Assert
            Assert.Equal(senderId, metadata.SenderId);
            Assert.Equal(receiverId, metadata.ReceiverId);
            Assert.Equal(timestamp, metadata.Timestamp);
            Assert.Null(metadata.CorrelationId);
            Assert.Null(metadata.ReplyTo);
            Assert.Equal(0, metadata.Priority);
            Assert.Null(metadata.ExpiresAt);
            Assert.Equal(messageType, metadata.MessageType);
            Assert.Equal(version, metadata.Version);
        }

        [Theory]
        [InlineData("")]
        [InlineData("simple-id")]
        [InlineData("complex-actor-id-with-dashes-123")]
        [InlineData("actor.with.dots")]
        [InlineData("actor_with_underscores")]
        public void MessageMetadata_ShouldSupportVariousActorIdFormats(string actorId)
        {
            // Arrange & Act
            var metadata = new TestMessageMetadata(
                actorId, "receiver", DateTimeOffset.UtcNow, "TestMessage", "1.0");

            // Assert
            Assert.Equal(actorId, metadata.SenderId);

            // Test with receiver ID as well
            var metadata2 = new TestMessageMetadata(
                "sender", actorId, DateTimeOffset.UtcNow, "TestMessage", "1.0");
            Assert.Equal(actorId, metadata2.ReceiverId);
        }

        [Theory]
        [InlineData(-10)]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(100)]
        public void MessageMetadata_ShouldSupportVariousPriorityLevels(int priority)
        {
            // Arrange & Act
            var metadata = new TestMessageMetadata(
                "sender", "receiver", DateTimeOffset.UtcNow, "TestMessage", "1.0",
                priority: priority);

            // Assert
            Assert.Equal(priority, metadata.Priority);
        }

        [Fact]
        public void MessageMetadata_ShouldHandleUtcTimestamps()
        {
            // Arrange
            var utcTimestamp = DateTimeOffset.UtcNow;

            // Act
            var metadata = new TestMessageMetadata(
                "sender", "receiver", utcTimestamp, "TestMessage", "1.0");

            // Assert
            Assert.Equal(utcTimestamp, metadata.Timestamp);
            // DateTimeOffset preserves timezone information, so we check the offset instead
            Assert.Equal(TimeSpan.Zero, metadata.Timestamp.Offset);
        }

        [Fact]
        public void MessageMetadata_ShouldHandleLocalTimestamps()
        {
            // Arrange
            var localTimestamp = DateTimeOffset.Now;

            // Act
            var metadata = new TestMessageMetadata(
                "sender", "receiver", localTimestamp, "TestMessage", "1.0");

            // Assert
            Assert.Equal(localTimestamp, metadata.Timestamp);
        }

        [Fact]
        public void MessageMetadata_ShouldHandleExpirationTimes()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddMinutes(30);

            // Act
            var metadata = new TestMessageMetadata(
                "sender", "receiver", now, "TestMessage", "1.0",
                expiresAt: expiresAt);

            // Assert
            Assert.Equal(expiresAt, metadata.ExpiresAt);
            Assert.True(metadata.ExpiresAt > metadata.Timestamp);
        }

        [Fact]
        public void MessageMetadata_ShouldHandleNullExpirationTime()
        {
            // Arrange & Act
            var metadata = new TestMessageMetadata(
                "sender", "receiver", DateTimeOffset.UtcNow, "TestMessage", "1.0");

            // Assert
            Assert.Null(metadata.ExpiresAt);
        }

        [Theory]
        [InlineData("")]
        [InlineData("simple-correlation")]
        [InlineData("guid-like-correlation-12345678-1234-1234-1234-123456789012")]
        [InlineData("request-response-123")]
        public void MessageMetadata_ShouldSupportVariousCorrelationIdFormats(string correlationId)
        {
            // Arrange & Act
            var metadata = new TestMessageMetadata(
                "sender", "receiver", DateTimeOffset.UtcNow, "TestMessage", "1.0",
                correlationId: correlationId);

            // Assert
            Assert.Equal(correlationId, metadata.CorrelationId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("reply-actor")]
        [InlineData("complex.reply.actor.address")]
        [InlineData("temporary-reply-queue-123")]
        public void MessageMetadata_ShouldSupportVariousReplyToFormats(string replyTo)
        {
            // Arrange & Act
            var metadata = new TestMessageMetadata(
                "sender", "receiver", DateTimeOffset.UtcNow, "TestMessage", "1.0",
                replyTo: replyTo);

            // Assert
            Assert.Equal(replyTo, metadata.ReplyTo);
        }

        [Theory]
        [InlineData("SimpleMessage")]
        [InlineData("Complex.Namespace.MessageType")]
        [InlineData("MessageType_With_Underscores")]
        [InlineData("MessageType-With-Dashes")]
        [InlineData("MessageTypeWithNumbers123")]
        public void MessageMetadata_ShouldSupportVariousMessageTypes(string messageType)
        {
            // Arrange & Act
            var metadata = new TestMessageMetadata(
                "sender", "receiver", DateTimeOffset.UtcNow, messageType, "1.0");

            // Assert
            Assert.Equal(messageType, metadata.MessageType);
        }

        [Theory]
        [InlineData("1.0")]
        [InlineData("2.1.3")]
        [InlineData("1.0.0-beta")]
        [InlineData("2.0.0-alpha.1")]
        [InlineData("v1")]
        [InlineData("latest")]
        public void MessageMetadata_ShouldSupportVariousVersionFormats(string version)
        {
            // Arrange & Act
            var metadata = new TestMessageMetadata(
                "sender", "receiver", DateTimeOffset.UtcNow, "TestMessage", version);

            // Assert
            Assert.Equal(version, metadata.Version);
        }

        [Fact]
        public void MessageMetadata_ShouldSupportCompleteScenario()
        {
            // Arrange - Simulate a complete request-response scenario
            var senderId = "user-service-001";
            var receiverId = "order-service-002";
            var timestamp = DateTimeOffset.UtcNow;
            var correlationId = Guid.NewGuid().ToString();
            var replyTo = "user-service-001-reply-queue";
            var priority = 3;
            var expiresAt = timestamp.AddMinutes(15);
            var messageType = "CreateOrderRequest";
            var version = "2.1.0";

            // Act
            var metadata = new TestMessageMetadata(
                senderId, receiverId, timestamp, messageType, version,
                correlationId, replyTo, priority, expiresAt);

            // Assert - Verify all properties are set correctly
            Assert.Equal(senderId, metadata.SenderId);
            Assert.Equal(receiverId, metadata.ReceiverId);
            Assert.Equal(timestamp, metadata.Timestamp);
            Assert.Equal(correlationId, metadata.CorrelationId);
            Assert.Equal(replyTo, metadata.ReplyTo);
            Assert.Equal(priority, metadata.Priority);
            Assert.Equal(expiresAt, metadata.ExpiresAt);
            Assert.Equal(messageType, metadata.MessageType);
            Assert.Equal(version, metadata.Version);

            // Verify logical relationships
            Assert.True(metadata.ExpiresAt > metadata.Timestamp);
            Assert.NotNull(metadata.CorrelationId);
            Assert.NotEmpty(metadata.CorrelationId);
        }

        [Fact]
        public void MessageMetadata_ShouldSupportMinimalScenario()
        {
            // Arrange - Simulate a minimal fire-and-forget message
            var senderId = "notification-service";
            var receiverId = "email-service";
            var timestamp = DateTimeOffset.UtcNow;
            var messageType = "SendEmailNotification";
            var version = "1.0";

            // Act
            var metadata = new TestMessageMetadata(
                senderId, receiverId, timestamp, messageType, version);

            // Assert - Verify required properties are set, optional ones are null/default
            Assert.Equal(senderId, metadata.SenderId);
            Assert.Equal(receiverId, metadata.ReceiverId);
            Assert.Equal(timestamp, metadata.Timestamp);
            Assert.Equal(messageType, metadata.MessageType);
            Assert.Equal(version, metadata.Version);
            
            // Optional properties should be null or default
            Assert.Null(metadata.CorrelationId);
            Assert.Null(metadata.ReplyTo);
            Assert.Equal(0, metadata.Priority);
            Assert.Null(metadata.ExpiresAt);
        }
    }
} 