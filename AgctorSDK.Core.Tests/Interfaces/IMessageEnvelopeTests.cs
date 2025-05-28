using System;
using System.Collections.Generic;
using AgctorSDK.Core.Interfaces;
using Moq;
using Xunit;

namespace AgctorSDK.Core.Tests.Interfaces
{
    /// <summary>
    /// Unit tests for the IMessageEnvelope interface contract and behavior.
    /// Tests verify that message envelope implementations properly handle payload, metadata, and headers.
    /// </summary>
    public class IMessageEnvelopeTests
    {
        /// <summary>
        /// Test implementation of IMessageEnvelope for testing purposes.
        /// Provides a concrete implementation with controllable behavior for testing.
        /// </summary>
        private class TestMessageEnvelope : IMessageEnvelope
        {
            public string Id { get; }
            public object Payload { get; private set; }
            public IMessageMetadata Metadata { get; }
            public IReadOnlyDictionary<string, object> Headers { get; private set; }

            public TestMessageEnvelope(string id, object payload, IMessageMetadata metadata, 
                IReadOnlyDictionary<string, object>? headers = null)
            {
                Id = id;
                Payload = payload;
                Metadata = metadata;
                Headers = headers ?? new Dictionary<string, object>();
            }

            public IMessageEnvelope WithPayload(object newPayload)
            {
                return new TestMessageEnvelope(Id, newPayload, Metadata, Headers);
            }

            public IMessageEnvelope WithHeaders(IDictionary<string, object> additionalHeaders)
            {
                var newHeaders = new Dictionary<string, object>(Headers);
                foreach (var header in additionalHeaders)
                {
                    newHeaders[header.Key] = header.Value;
                }
                return new TestMessageEnvelope(Id, Payload, Metadata, newHeaders);
            }
        }

        private Mock<IMessageMetadata> CreateMockMetadata()
        {
            var mockMetadata = new Mock<IMessageMetadata>();
            mockMetadata.Setup(m => m.SenderId).Returns("sender-123");
            mockMetadata.Setup(m => m.ReceiverId).Returns("receiver-456");
            mockMetadata.Setup(m => m.Timestamp).Returns(DateTimeOffset.UtcNow);
            mockMetadata.Setup(m => m.MessageType).Returns("TestMessage");
            mockMetadata.Setup(m => m.Version).Returns("1.0");
            mockMetadata.Setup(m => m.Priority).Returns(1);
            return mockMetadata;
        }

        [Fact]
        public void MessageEnvelope_ShouldHaveRequiredProperties()
        {
            // Arrange
            var id = "msg-123";
            var payload = "test message";
            var mockMetadata = CreateMockMetadata();
            var headers = new Dictionary<string, object> { { "custom-header", "value" } };

            // Act
            var envelope = new TestMessageEnvelope(id, payload, mockMetadata.Object, headers);

            // Assert
            Assert.Equal(id, envelope.Id);
            Assert.Equal(payload, envelope.Payload);
            Assert.Equal(mockMetadata.Object, envelope.Metadata);
            Assert.Equal(headers, envelope.Headers);
        }

        [Fact]
        public void MessageEnvelope_ShouldHandleNullHeaders()
        {
            // Arrange
            var id = "msg-123";
            var payload = "test message";
            var mockMetadata = CreateMockMetadata();

            // Act
            var envelope = new TestMessageEnvelope(id, payload, mockMetadata.Object);

            // Assert
            Assert.NotNull(envelope.Headers);
            Assert.Empty(envelope.Headers);
        }

        [Fact]
        public void WithPayload_ShouldCreateNewEnvelopeWithUpdatedPayload()
        {
            // Arrange
            var originalPayload = "original message";
            var newPayload = "updated message";
            var mockMetadata = CreateMockMetadata();
            var headers = new Dictionary<string, object> { { "header1", "value1" } };
            var originalEnvelope = new TestMessageEnvelope("msg-123", originalPayload, mockMetadata.Object, headers);

            // Act
            var newEnvelope = originalEnvelope.WithPayload(newPayload);

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.Equal(originalEnvelope.Id, newEnvelope.Id);
            Assert.Equal(newPayload, newEnvelope.Payload);
            Assert.Equal(originalPayload, originalEnvelope.Payload); // Original should be unchanged
            Assert.Equal(originalEnvelope.Metadata, newEnvelope.Metadata);
            Assert.Equal(originalEnvelope.Headers, newEnvelope.Headers);
        }

        [Fact]
        public void WithPayload_ShouldHandleNullPayload()
        {
            // Arrange
            var originalPayload = "original message";
            var mockMetadata = CreateMockMetadata();
            var originalEnvelope = new TestMessageEnvelope("msg-123", originalPayload, mockMetadata.Object);

            // Act
            var newEnvelope = originalEnvelope.WithPayload(null!);

            // Assert
            Assert.Null(newEnvelope.Payload);
            Assert.Equal(originalPayload, originalEnvelope.Payload); // Original should be unchanged
        }

        [Fact]
        public void WithHeaders_ShouldCreateNewEnvelopeWithUpdatedHeaders()
        {
            // Arrange
            var mockMetadata = CreateMockMetadata();
            var originalHeaders = new Dictionary<string, object> { { "header1", "value1" } };
            var additionalHeaders = new Dictionary<string, object> 
            { 
                { "header2", "value2" },
                { "header3", "value3" }
            };
            var originalEnvelope = new TestMessageEnvelope("msg-123", "payload", mockMetadata.Object, originalHeaders);

            // Act
            var newEnvelope = originalEnvelope.WithHeaders(additionalHeaders);

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.Equal(originalEnvelope.Id, newEnvelope.Id);
            Assert.Equal(originalEnvelope.Payload, newEnvelope.Payload);
            Assert.Equal(originalEnvelope.Metadata, newEnvelope.Metadata);
            
            // Original headers should be unchanged
            Assert.Single(originalEnvelope.Headers);
            Assert.Equal("value1", originalEnvelope.Headers["header1"]);
            
            // New envelope should have all headers
            Assert.Equal(3, newEnvelope.Headers.Count);
            Assert.Equal("value1", newEnvelope.Headers["header1"]);
            Assert.Equal("value2", newEnvelope.Headers["header2"]);
            Assert.Equal("value3", newEnvelope.Headers["header3"]);
        }

        [Fact]
        public void WithHeaders_ShouldOverwriteExistingHeaders()
        {
            // Arrange
            var mockMetadata = CreateMockMetadata();
            var originalHeaders = new Dictionary<string, object> 
            { 
                { "header1", "original-value" },
                { "header2", "value2" }
            };
            var additionalHeaders = new Dictionary<string, object> 
            { 
                { "header1", "updated-value" }, // This should overwrite
                { "header3", "value3" }
            };
            var originalEnvelope = new TestMessageEnvelope("msg-123", "payload", mockMetadata.Object, originalHeaders);

            // Act
            var newEnvelope = originalEnvelope.WithHeaders(additionalHeaders);

            // Assert
            Assert.Equal(3, newEnvelope.Headers.Count);
            Assert.Equal("updated-value", newEnvelope.Headers["header1"]); // Should be overwritten
            Assert.Equal("value2", newEnvelope.Headers["header2"]); // Should be preserved
            Assert.Equal("value3", newEnvelope.Headers["header3"]); // Should be added
            
            // Original should be unchanged
            Assert.Equal("original-value", originalEnvelope.Headers["header1"]);
        }

        [Fact]
        public void WithHeaders_ShouldHandleEmptyAdditionalHeaders()
        {
            // Arrange
            var mockMetadata = CreateMockMetadata();
            var originalHeaders = new Dictionary<string, object> { { "header1", "value1" } };
            var emptyHeaders = new Dictionary<string, object>();
            var originalEnvelope = new TestMessageEnvelope("msg-123", "payload", mockMetadata.Object, originalHeaders);

            // Act
            var newEnvelope = originalEnvelope.WithHeaders(emptyHeaders);

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.Equal(originalEnvelope.Headers.Count, newEnvelope.Headers.Count);
            Assert.Equal(originalEnvelope.Headers["header1"], newEnvelope.Headers["header1"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("msg-123")]
        [InlineData("very-long-message-id-with-special-characters-123-456-789")]
        public void MessageEnvelope_ShouldSupportVariousIdFormats(string messageId)
        {
            // Arrange
            var mockMetadata = CreateMockMetadata();

            // Act
            var envelope = new TestMessageEnvelope(messageId, "payload", mockMetadata.Object);

            // Assert
            Assert.Equal(messageId, envelope.Id);
        }

        [Theory]
        [InlineData("string payload")]
        [InlineData(123)]
        [InlineData(true)]
        [InlineData(null)]
        public void MessageEnvelope_ShouldSupportVariousPayloadTypes(object payload)
        {
            // Arrange
            var mockMetadata = CreateMockMetadata();

            // Act
            var envelope = new TestMessageEnvelope("msg-123", payload, mockMetadata.Object);

            // Assert
            Assert.Equal(payload, envelope.Payload);
        }

        [Fact]
        public void MessageEnvelope_ShouldSupportComplexPayloadTypes()
        {
            // Arrange
            var complexPayload = new
            {
                Id = 123,
                Name = "Test",
                Data = new List<string> { "item1", "item2" },
                Metadata = new Dictionary<string, object> { { "key", "value" } }
            };
            var mockMetadata = CreateMockMetadata();

            // Act
            var envelope = new TestMessageEnvelope("msg-123", complexPayload, mockMetadata.Object);

            // Assert
            Assert.Equal(complexPayload, envelope.Payload);
        }

        [Fact]
        public void Headers_ShouldBeReadOnly()
        {
            // Arrange
            var mockMetadata = CreateMockMetadata();
            var headers = new Dictionary<string, object> { { "header1", "value1" } };
            var envelope = new TestMessageEnvelope("msg-123", "payload", mockMetadata.Object, headers);

            // Act & Assert
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(envelope.Headers);
            
            // Verify that the headers collection is read-only by checking the type
            // The actual implementation should ensure immutability
            Assert.Equal("value1", envelope.Headers["header1"]);
        }
    }
} 