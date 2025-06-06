using System;
using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages; // Using the concrete implementation for some tests or as a reference
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Interfaces
{
    /// <summary>
    /// Unit tests for the IMessageEnvelope interface contract and behavior.
    /// Tests verify that message envelope implementations properly handle payload, metadata, and headers according to MCP.
    /// </summary>
    [TestClass]
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

        [TestMethod]
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
            Assert.AreEqual(id, envelope.Id);
            Assert.AreEqual(payload, envelope.Payload);
            Assert.AreEqual(metadata.Count, envelope.Metadata.Count);
            Assert.AreEqual(metadata["Timestamp"], envelope.Metadata["Timestamp"]);
            Assert.AreEqual(headers.Count, envelope.Headers.Count);
            Assert.AreEqual(headers["SenderId"], envelope.Headers["SenderId"]);
        }

        [TestMethod]
        public void MessageEnvelope_ShouldHandleNullMetadataAndHeaders_MCP()
        {
            // Arrange
            var id = "msg-123";
            var payload = "test message";

            // Act
            var envelope = new TestMessageEnvelope(id, payload, null, null);

            // Assert
            Assert.IsNotNull(envelope.Metadata);
            Assert.AreEqual(0, envelope.Metadata.Count);
            Assert.IsNotNull(envelope.Headers);
            Assert.AreEqual(0, envelope.Headers.Count);
        }

        [TestMethod]
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
            Assert.AreNotSame(originalEnvelope, newEnvelope);
            Assert.AreEqual(originalEnvelope.Id, newEnvelope.Id);
            Assert.AreEqual(newPayload, newEnvelope.Payload);
            Assert.AreEqual(originalPayload, originalEnvelope.Payload); // Original should be unchanged
            CollectionAssert.AreEqual(originalEnvelope.Metadata.ToList(), newEnvelope.Metadata.ToList()); // Collections should be equivalent by content if copied correctly
            CollectionAssert.AreEqual(originalEnvelope.Headers.ToList(), newEnvelope.Headers.ToList());   // Collections should be equivalent by content if copied correctly
        }


        [TestMethod]
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
            Assert.AreNotSame(originalEnvelope, newEnvelope);
            Assert.AreEqual(originalEnvelope.Id, newEnvelope.Id);
            Assert.AreEqual(originalEnvelope.Payload, newEnvelope.Payload);
            Assert.AreEqual(originalEnvelope.Metadata.Count, newEnvelope.Metadata.Count); // Metadata should be unchanged
            
            Assert.AreEqual(2, newEnvelope.Headers.Count); // Should only contain replacement headers
            Assert.AreEqual("newValue", newEnvelope.Headers["newHeader"]);
            Assert.AreEqual("updatedValue", newEnvelope.Headers["headerToReplace"]);
            Assert.IsFalse(newEnvelope.Headers.ContainsKey("header1")); // Original header1 should be gone

            // Original headers should be unchanged
            Assert.AreEqual(2, originalEnvelope.Headers.Count);
            Assert.AreEqual("value1", originalEnvelope.Headers["header1"]);
            Assert.AreEqual("oldValue", originalEnvelope.Headers["headerToReplace"]);
        }

        [TestMethod]
        public void WithHeaders_ShouldHandleNullReplacementHeaders_MCP()
        {
            // Arrange
            var originalHeaders = new Dictionary<string, string> { { "header1", "value1" } };
            var originalEnvelope = new TestMessageEnvelope("msg-123", "payload", null, originalHeaders);

            // Act
            var newEnvelope = originalEnvelope.WithHeaders(null!); // Pass null for replacement

            // Assert
            Assert.IsNotNull(newEnvelope.Headers); // Headers should be an empty dictionary, not null
            Assert.AreEqual(0, newEnvelope.Headers.Count);     // All original headers should be gone

            // Original headers should be unchanged
            Assert.AreEqual(1, originalEnvelope.Headers.Count);
            Assert.AreEqual("value1", originalEnvelope.Headers["header1"]);
        }

        [TestMethod]
        public void WithHeader_ShouldAddNewHeaderIfNotExists_MCP()
        {
            // Arrange
            var originalEnvelope = new TestMessageEnvelope("id", "payload");

            // Act
            var newEnvelope = originalEnvelope.WithHeader("newKey", "newValue");

            // Assert
            Assert.AreNotSame(originalEnvelope, newEnvelope);
            Assert.IsTrue(newEnvelope.Headers.ContainsKey("newKey"));
            Assert.AreEqual("newValue", newEnvelope.Headers["newKey"]);
            Assert.AreEqual(0, originalEnvelope.Headers.Count);
        }

        [TestMethod]
        public void WithHeader_ShouldUpdateExistingHeader_MCP()
        {
            // Arrange
            var originalHeaders = new Dictionary<string, string> { { "existingKey", "originalValue" } };
            var originalEnvelope = new TestMessageEnvelope("id", "payload", null, originalHeaders);

            // Act
            var newEnvelope = originalEnvelope.WithHeader("existingKey", "updatedValue");

            // Assert
            Assert.AreNotSame(originalEnvelope, newEnvelope);
            Assert.AreEqual("updatedValue", newEnvelope.Headers["existingKey"]);
            Assert.AreEqual(1, newEnvelope.Headers.Count);
            Assert.AreEqual("originalValue", originalEnvelope.Headers["existingKey"]); // Original unchanged
        }

        [TestMethod]
        public void WithMetadata_ShouldReplaceAllMetadata_MCP()
        {
            // Arrange
            var originalMetadata = new Dictionary<string, object> { { "meta1", "val1" } };
            var replacementMetadata = new Dictionary<string, object> { { "newMeta", "newVal" }, { "meta1", "updatedVal"} };
            var originalEnvelope = new TestMessageEnvelope("id", "payload", originalMetadata);

            // Act
            var newEnvelope = originalEnvelope.WithMetadata(replacementMetadata);

            // Assert
            Assert.AreNotSame(originalEnvelope, newEnvelope);
            Assert.AreEqual(2, newEnvelope.Metadata.Count);
            Assert.AreEqual("newVal", newEnvelope.Metadata["newMeta"]);
            Assert.AreEqual("updatedVal", newEnvelope.Metadata["meta1"]);
            Assert.AreEqual(1, originalEnvelope.Metadata.Count); // Original unchanged
        }
        
        [TestMethod]
        public void WithMetadata_ShouldHandleNullReplacementMetadata_MCP()
        {
            // Arrange
            var originalMetadata = new Dictionary<string, object> { { "meta1", "val1" } };
            var originalEnvelope = new TestMessageEnvelope("id", "payload", originalMetadata);

            // Act
            var newEnvelope = originalEnvelope.WithMetadata(null!); // Pass null for replacement

            // Assert
            Assert.IsNotNull(newEnvelope.Metadata); // Metadata should be an empty dictionary, not null
            Assert.AreEqual(0, newEnvelope.Metadata.Count);     // All original metadata should be gone

            // Original metadata should be unchanged
            Assert.AreEqual(1, originalEnvelope.Metadata.Count);
            Assert.AreEqual("val1", originalEnvelope.Metadata["meta1"]);
        }

        [TestMethod]
        public void WithMetadata_ShouldAddNewEntryIfNotExists_MCP()
        {
            // Arrange
            var originalEnvelope = new TestMessageEnvelope("id", "payload");

            // Act
            var newEnvelope = originalEnvelope.WithMetadata("newKey", "newValue");

            // Assert
            Assert.AreNotSame(originalEnvelope, newEnvelope);
            Assert.IsTrue(newEnvelope.Metadata.ContainsKey("newKey"));
            Assert.AreEqual("newValue", newEnvelope.Metadata["newKey"]);
            Assert.AreEqual(0, originalEnvelope.Metadata.Count); // Original unchanged
        }

        [TestMethod]
        public void WithMetadata_ShouldUpdateExistingEntry_MCP()
        {
            // Arrange
            var originalMetadata = new Dictionary<string, object> { { "existingKey", "originalValue" } };
            var originalEnvelope = new TestMessageEnvelope("id", "payload", originalMetadata);

            // Act
            var newEnvelope = originalEnvelope.WithMetadata("existingKey", "updatedValue");

            // Assert
            Assert.AreNotSame(originalEnvelope, newEnvelope);
            Assert.AreEqual("updatedValue", newEnvelope.Metadata["existingKey"]);
            Assert.AreEqual(1, newEnvelope.Metadata.Count);
            Assert.AreEqual("originalValue", originalEnvelope.Metadata["existingKey"]); // Original unchanged
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("msg-123")]
        [DataRow("very-long-message-id-with-special-characters-123-456-789")]
        public void MessageEnvelope_ShouldSupportVariousIdFormats(string messageId)
        {
            // Arrange & Act
            var envelope = new TestMessageEnvelope(messageId, "payload");

            // Assert
            Assert.AreEqual(messageId, envelope.Id);
        }

        [DataTestMethod]
        [DataRow("string payload")]
        [DataRow(123)]
        [DataRow(true)]
        [DataRow(null)]
        public void MessageEnvelope_ShouldSupportVariousPayloadTypes(object payload)
        {
            // Arrange & Act
            var envelope = new TestMessageEnvelope("id", payload);

            // Assert
            Assert.AreEqual(payload, envelope.Payload);
        }

        private class ComplexPayload { public string Data { get; set; } = string.Empty; public int Value { get; set; } }
        [TestMethod]
        public void MessageEnvelope_ShouldSupportComplexPayloadTypes()
        {
            // Arrange
            var complexPayload = new ComplexPayload { Data = "TestData", Value = 123 };
            
            // Act
            var envelope = new TestMessageEnvelope("id", complexPayload);

            // Assert
            Assert.IsInstanceOfType(envelope.Payload, typeof(ComplexPayload));
            var retrievedPayload = envelope.Payload as ComplexPayload;
            Assert.IsNotNull(retrievedPayload);
            Assert.AreEqual("TestData", retrievedPayload.Data);
            Assert.AreEqual(123, retrievedPayload.Value);
        }

        [TestMethod]
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
            Assert.ThrowsException<NotSupportedException>(() => ((IDictionary<string, string>)envelope.Headers).Add("newKey", "newValue"));
            Assert.ThrowsException<NotSupportedException>(() => ((IDictionary<string, string>)envelope.Headers).Clear());
            Assert.ThrowsException<NotSupportedException>(() => ((IDictionary<string, string>)envelope.Headers).Remove("key"));
        }
        
        [TestMethod]
        public void Metadata_IsModifiable_WhenAccessedViaInterfaceProperty_MCP()
        {
            // Arrange
            var metadata = new Dictionary<string, object> { { "key", "value" } };
            IMessageEnvelope envelope = new AgctorSDK.Core.Messages.MessageEnvelope("payload", metadata, "id", null);

            // Act
            var retrievedMetadata = envelope.Metadata; // IDictionary<string, object> is modifiable by definition
            retrievedMetadata.Add("newKey", "newValue");
            retrievedMetadata["key"] = "updatedValue";

            // Assert
            Assert.AreEqual("newValue", envelope.Metadata["newKey"]);
            Assert.AreEqual("updatedValue", envelope.Metadata["key"]);
            // This test also shows that the internal dictionary is returned directly by the property, not a copy.
            // This is consistent with IDictionary<TKey, TValue> properties.
        }

    }
} 