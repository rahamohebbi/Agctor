using System;
using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages; // Using the concrete implementation for some tests or as a reference
using Xunit;

namespace AgctorSDK.Core.Tests.Interfaces
{
    /// <summary>
    /// Unit tests for the IMessageEnvelope interface contract and behavior.
    /// Tests verify that message envelope implementations properly handle payload, metadata, and headers according to MCP.
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
            public IDictionary<string, object> Metadata { get; private set; } // Changed from IMessageMetadata
            public IReadOnlyDictionary<string, string> Headers { get; private set; } // Changed from IReadOnlyDictionary<string, object>

            public TestMessageEnvelope(string id, object payload, 
                IDictionary<string, object>? metadata = null, // Changed from IMessageMetadata
                IReadOnlyDictionary<string, string>? headers = null) // Changed from IReadOnlyDictionary<string, object>
            {
                Id = id;
                Payload = payload;
                Metadata = metadata != null ? new Dictionary<string, object>(metadata) : new Dictionary<string, object>();
                Headers = headers != null ? new Dictionary<string, string>(headers) : new Dictionary<string, string>();
            }

            public IMessageEnvelope WithPayload(object newPayload)
            {
                return new TestMessageEnvelope(Id, newPayload, Metadata, Headers);
            }

            public IMessageEnvelope WithHeaders(IDictionary<string, string> replacementHeaders)
            {
                // replacementHeaders can be null, constructor handles it
                return new TestMessageEnvelope(Id, Payload, Metadata, new Dictionary<string, string>(replacementHeaders ?? new Dictionary<string, string>()));
            }

            public IMessageEnvelope WithHeader(string key, string value)
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                var newHeaders = new Dictionary<string, string>(Headers);
                newHeaders[key] = value;
                return new TestMessageEnvelope(Id, Payload, Metadata, newHeaders);
            }

            public IMessageEnvelope WithMetadata(IDictionary<string, object> replacementMetadata)
            {
                 // replacementMetadata can be null, constructor handles it
                return new TestMessageEnvelope(Id, Payload, new Dictionary<string, object>(replacementMetadata ?? new Dictionary<string, object>()), Headers);
            }

            public IMessageEnvelope WithMetadata(string key, object value)
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                var newMetadata = new Dictionary<string, object>(Metadata);
                newMetadata[key] = value;
                return new TestMessageEnvelope(Id, Payload, newMetadata, Headers);
            }
        }

        // Removed CreateMockMetadata() as IMessageMetadata is gone.

        [Fact]
        public void MessageEnvelope_ShouldHaveRequiredProperties_MCP()
        {
            // Arrange
            var id = "msg-123";
            var payload = "test message";
            var metadata = new Dictionary<string, object> { { "Timestamp", DateTimeOffset.UtcNow }, { "Priority", "High" } };
            var headers = new Dictionary<string, string> { { "SenderId", "sender-abc" }, { "MessageType", "TestCommand" } };

            // Act
            // Using the concrete AgctorSDK.Core.Messages.MessageEnvelope for this test to ensure its constructor works as expected.
            var envelope = new AgctorSDK.Core.Messages.MessageEnvelope(payload, metadata, id, headers);

            // Assert
            Assert.Equal(id, envelope.Id);
            Assert.Equal(payload, envelope.Payload);
            Assert.Equal(metadata.Count, envelope.Metadata.Count);
            Assert.Equal(metadata["Timestamp"], envelope.Metadata["Timestamp"]);
            Assert.Equal(headers.Count, envelope.Headers.Count);
            Assert.Equal(headers["SenderId"], envelope.Headers["SenderId"]);
        }

        [Fact]
        public void MessageEnvelope_ShouldHandleNullMetadataAndHeaders_MCP()
        {
            // Arrange
            var id = "msg-123";
            var payload = "test message";

            // Act
            var envelope = new TestMessageEnvelope(id, payload, null, null);

            // Assert
            Assert.NotNull(envelope.Metadata);
            Assert.Empty(envelope.Metadata);
            Assert.NotNull(envelope.Headers);
            Assert.Empty(envelope.Headers);
        }

        [Fact]
        public void WithPayload_ShouldCreateNewEnvelopeWithUpdatedPayload_MCP()
        {
            // Arrange
            var originalPayload = "original message";
            var newPayload = "updated message";
            var metadata = new Dictionary<string, object> { { "CorrelationId", "corr-123" } };
            var headers = new Dictionary<string, string> { { "Content-Type", "application/json" } };
            var originalEnvelope = new TestMessageEnvelope("msg-123", originalPayload, metadata, headers);

            // Act
            var newEnvelope = originalEnvelope.WithPayload(newPayload);

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.Equal(originalEnvelope.Id, newEnvelope.Id);
            Assert.Equal(newPayload, newEnvelope.Payload);
            Assert.Equal(originalPayload, originalEnvelope.Payload); // Original should be unchanged
            Assert.Equal(originalEnvelope.Metadata, newEnvelope.Metadata); // Collections should be equivalent by content if copied correctly
            Assert.Equal(originalEnvelope.Headers, newEnvelope.Headers);   // Collections should be equivalent by content if copied correctly
        }


        [Fact]
        public void WithHeaders_ShouldReplaceAllHeaders_MCP()
        {
            // Arrange
            var originalHeaders = new Dictionary<string, string> { { "header1", "value1" }, { "headerToReplace", "oldValue" } };
            var replacementHeaders = new Dictionary<string, string> 
            { 
                { "newHeader", "newValue" },
                { "headerToReplace", "updatedValue" }
            };
            var originalEnvelope = new TestMessageEnvelope("msg-123", "payload", new Dictionary<string, object>(), originalHeaders);

            // Act
            var newEnvelope = originalEnvelope.WithHeaders(replacementHeaders);

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.Equal(originalEnvelope.Id, newEnvelope.Id);
            Assert.Equal(originalEnvelope.Payload, newEnvelope.Payload);
            Assert.Equal(originalEnvelope.Metadata.Count, newEnvelope.Metadata.Count); // Metadata should be unchanged
            
            Assert.Equal(2, newEnvelope.Headers.Count); // Should only contain replacement headers
            Assert.Equal("newValue", newEnvelope.Headers["newHeader"]);
            Assert.Equal("updatedValue", newEnvelope.Headers["headerToReplace"]);
            Assert.False(newEnvelope.Headers.ContainsKey("header1")); // Original header1 should be gone

            // Original headers should be unchanged
            Assert.Equal(2, originalEnvelope.Headers.Count);
            Assert.Equal("value1", originalEnvelope.Headers["header1"]);
            Assert.Equal("oldValue", originalEnvelope.Headers["headerToReplace"]);
        }

        [Fact]
        public void WithHeaders_ShouldHandleNullReplacementHeaders_MCP()
        {
            // Arrange
            var originalHeaders = new Dictionary<string, string> { { "header1", "value1" } };
            var originalEnvelope = new TestMessageEnvelope("msg-123", "payload", null, originalHeaders);

            // Act
            var newEnvelope = originalEnvelope.WithHeaders(null!); // Pass null for replacement

            // Assert
            Assert.NotNull(newEnvelope.Headers); // Headers should be an empty dictionary, not null
            Assert.Empty(newEnvelope.Headers);     // All original headers should be gone

            // Original headers should be unchanged
            Assert.Single(originalEnvelope.Headers);
            Assert.Equal("value1", originalEnvelope.Headers["header1"]);
        }

        [Fact]
        public void WithHeader_ShouldAddNewHeaderIfNotExists_MCP()
        {
            // Arrange
            var originalEnvelope = new TestMessageEnvelope("id", "payload");

            // Act
            var newEnvelope = originalEnvelope.WithHeader("newKey", "newValue");

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.True(newEnvelope.Headers.ContainsKey("newKey"));
            Assert.Equal("newValue", newEnvelope.Headers["newKey"]);
            Assert.Empty(originalEnvelope.Headers);
        }

        [Fact]
        public void WithHeader_ShouldUpdateExistingHeader_MCP()
        {
            // Arrange
            var originalHeaders = new Dictionary<string, string> { { "existingKey", "originalValue" } };
            var originalEnvelope = new TestMessageEnvelope("id", "payload", null, originalHeaders);

            // Act
            var newEnvelope = originalEnvelope.WithHeader("existingKey", "updatedValue");

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.Equal("updatedValue", newEnvelope.Headers["existingKey"]);
            Assert.Single(newEnvelope.Headers);
            Assert.Equal("originalValue", originalEnvelope.Headers["existingKey"]); // Original unchanged
        }

        [Fact]
        public void WithMetadata_ShouldReplaceAllMetadata_MCP()
        {
            // Arrange
            var originalMetadata = new Dictionary<string, object> { { "meta1", "val1" } };
            var replacementMetadata = new Dictionary<string, object> { { "newMeta", "newVal" }, { "meta1", "updatedVal"} };
            var originalEnvelope = new TestMessageEnvelope("id", "payload", originalMetadata);

            // Act
            var newEnvelope = originalEnvelope.WithMetadata(replacementMetadata);

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.Equal(2, newEnvelope.Metadata.Count);
            Assert.Equal("newVal", newEnvelope.Metadata["newMeta"]);
            Assert.Equal("updatedVal", newEnvelope.Metadata["meta1"]);
            Assert.Single(originalEnvelope.Metadata); // Original unchanged
        }
        
        [Fact]
        public void WithMetadata_ShouldHandleNullReplacementMetadata_MCP()
        {
            // Arrange
            var originalMetadata = new Dictionary<string, object> { { "meta1", "val1" } };
            var originalEnvelope = new TestMessageEnvelope("id", "payload", originalMetadata);

            // Act
            var newEnvelope = originalEnvelope.WithMetadata(null!); // Pass null for replacement

            // Assert
            Assert.NotNull(newEnvelope.Metadata); // Metadata should be an empty dictionary, not null
            Assert.Empty(newEnvelope.Metadata);     // All original metadata should be gone

            // Original metadata should be unchanged
            Assert.Single(originalEnvelope.Metadata);
            Assert.Equal("val1", originalEnvelope.Metadata["meta1"]);
        }

        [Fact]
        public void WithMetadata_ShouldAddNewEntryIfNotExists_MCP()
        {
            // Arrange
            var originalEnvelope = new TestMessageEnvelope("id", "payload");

            // Act
            var newEnvelope = originalEnvelope.WithMetadata("newKey", "newValue");

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.True(newEnvelope.Metadata.ContainsKey("newKey"));
            Assert.Equal("newValue", newEnvelope.Metadata["newKey"]);
            Assert.Empty(originalEnvelope.Metadata); // Original unchanged
        }

        [Fact]
        public void WithMetadata_ShouldUpdateExistingEntry_MCP()
        {
            // Arrange
            var originalMetadata = new Dictionary<string, object> { { "existingKey", "originalValue" } };
            var originalEnvelope = new TestMessageEnvelope("id", "payload", originalMetadata);

            // Act
            var newEnvelope = originalEnvelope.WithMetadata("existingKey", "updatedValue");

            // Assert
            Assert.NotSame(originalEnvelope, newEnvelope);
            Assert.Equal("updatedValue", newEnvelope.Metadata["existingKey"]);
            Assert.Single(newEnvelope.Metadata);
            Assert.Equal("originalValue", originalEnvelope.Metadata["existingKey"]); // Original unchanged
        }

        [Theory]
        [InlineData("")]
        [InlineData("msg-123")]
        [InlineData("very-long-message-id-with-special-characters-123-456-789")]
        public void MessageEnvelope_ShouldSupportVariousIdFormats(string messageId)
        {
            // Arrange & Act
            var envelope = new TestMessageEnvelope(messageId, "payload");

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
            // Arrange & Act
            var envelope = new TestMessageEnvelope("id", payload);

            // Assert
            Assert.Equal(payload, envelope.Payload);
        }

        private class ComplexPayload { public string Data { get; set; } = string.Empty; public int Value { get; set; } }
        [Fact]
        public void MessageEnvelope_ShouldSupportComplexPayloadTypes()
        {
            // Arrange
            var complexPayload = new ComplexPayload { Data = "TestData", Value = 123 };
            
            // Act
            var envelope = new TestMessageEnvelope("id", complexPayload);

            // Assert
            Assert.Same(complexPayload, envelope.Payload);
        }

        [Fact]
        public void Headers_ShouldBeReadOnly_WhenAccessedViaInterfaceProperty_MCP()
        {
            // Arrange
            var headers = new Dictionary<string, string> { { "key", "value" } };
            IMessageEnvelope envelope = new AgctorSDK.Core.Messages.MessageEnvelope("payload", null, "id", headers);

            // Act & Assert
            // The Headers property is IReadOnlyDictionary, so direct modification is a compile error.
            // We check if it throws if cast to a modifiable type and then modified.
            // This test depends on the concrete implementation's choice for the underlying collection.
            // AgctorSDK.Core.Messages.MessageEnvelope constructor makes a new Dictionary<string,string>() for Headers.
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)envelope.Headers).Add("newKey", "newValue"));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)envelope.Headers).Clear());
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)envelope.Headers).Remove("key"));
        }
        
        [Fact]
        public void Metadata_IsModifiable_WhenAccessedViaInterfaceProperty_MCP()
        {
            // Arrange
            var metadata = new Dictionary<string, object> { { "key", "value" } };
            IMessageEnvelope envelope = new AgctorSDK.Core.Messages.MessageEnvelope("payload", metadata, "id", null);

            // Act
            var retrievedMetadata = envelope.Metadata; // IDictionary<string, object> is modifiable by definition
            retrievedMetadata["newKey"] = "newValue";
            retrievedMetadata["key"] = "updatedValue";

            // Assert
            Assert.Equal("newValue", envelope.Metadata["newKey"]);
            Assert.Equal("updatedValue", envelope.Metadata["key"]);
            // This test also shows that the internal dictionary is returned directly by the property, not a copy.
            // This is consistent with IDictionary<TKey, TValue> properties.
        }

    }
} 