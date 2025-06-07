using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AgctorSDK.Core.Tests.Interfaces
{
    /// <summary>
    /// Unit tests for the IActorRuntimeAdapter interface contract and behavior.
    /// Tests verify that runtime adapter implementations properly handle actor lifecycle and messaging.
    /// </summary>
    [TestClass]
    public class IActorRuntimeAdapterTests
    {
        /// <summary>
        /// Test implementation of IActorRuntimeAdapter for testing purposes.
        /// Provides a concrete implementation with controllable behavior for testing.
        /// </summary>
        private class TestActorRuntimeAdapter : IActorRuntimeAdapter
        {
            public string Name { get; }
            public string Version { get; }
            public bool IsInitialized { get; private set; }
            public IReadOnlyDictionary<string, object> Configuration { get; private set; }

            // Test control properties
            public bool ShouldThrowOnInitialize { get; set; }
            public bool ShouldThrowOnShutdown { get; set; }
            public bool ShouldThrowOnSpawnActor { get; set; }
            public bool ShouldThrowOnSendMessage { get; set; }
            public List<string> SpawnedActorIds { get; } = new();
            public List<string> StoppedActorIds { get; } = new();
            public List<(string targetId, object message, string? senderId, IDictionary<string, string>? headers)> SentMessages { get; } = new();

            public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned;
            public event EventHandler<ActorStoppedEventArgs>? ActorStopped;
            public event EventHandler<MessageSentEventArgs>? MessageSent;

            public TestActorRuntimeAdapter(string name = "TestRuntime", string version = "1.0.0")
            {
                Name = name;
                Version = version;
                Configuration = new Dictionary<string, object>();
            }

            public Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (ShouldThrowOnInitialize)
                    throw new InvalidOperationException("Test exception during initialization");

                Configuration = new Dictionary<string, object>(configuration);
                IsInitialized = true;
                return Task.CompletedTask;
            }

            public Task ShutdownAsync(CancellationToken cancellationToken = default)
            {
                if (ShouldThrowOnShutdown)
                    throw new InvalidOperationException("Test exception during shutdown");

                IsInitialized = false;
                return Task.CompletedTask;
            }

            public Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (ShouldThrowOnSpawnActor)
                    throw new InvalidOperationException("Test exception during actor spawning");

                SpawnedActorIds.Add(actorId);
                
                // Create a real TestActor instance for predictable behavior
                T actor;
                if (typeof(T) == typeof(TestActor)) 
                {
                    var testActor = new TestActor(actorId);
                    actor = testActor as T;
                }
                else
                {
                    // Use Moq for other actor types
                    var mockActor = new Mock<T>();
                    var actorMock = mockActor.As<IActor>();
                    actorMock.Setup(a => a.Id).Returns(actorId);
                    actorMock.Setup(a => a.ActorType).Returns(typeof(T).Name);
                    actorMock.Setup(a => a.State).Returns(ActorState.Active);
                    actor = mockActor.Object;
                }

                ActorSpawned?.Invoke(this, new ActorSpawnedEventArgs(actorId, typeof(T).Name));
                return Task.FromResult(actor);
            }

            public Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> actorFactory, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
            {
                throw new NotImplementedException();
            }

            public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor
            {
                if (SpawnedActorIds.Contains(actorId) && !StoppedActorIds.Contains(actorId))
                {
                    // Create a real TestActor instance for predictable behavior
                    if (typeof(T) == typeof(TestActor)) 
                    {
                        var testActor = new TestActor(actorId);
                        return Task.FromResult(testActor as T);
                    }
                    else
                    {
                        // Use Moq for other actor types
                        var mockActor = new Mock<T>();
                        var actorMock = mockActor.As<IActor>();
                        actorMock.Setup(a => a.Id).Returns(actorId);
                        actorMock.Setup(a => a.ActorType).Returns(typeof(T).Name);
                        actorMock.Setup(a => a.State).Returns(ActorState.Active);
                        return Task.FromResult<T?>(mockActor.Object);
                    }
                }
                return Task.FromResult<T?>(null);
            }

            public Task SendMessageAsync(string targetActorId, object message, string? senderId = null, 
                IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
            {
                if (ShouldThrowOnSendMessage)
                    throw new InvalidOperationException("Test exception during message send");

                SentMessages.Add((targetActorId, message, senderId, headers));
                
                // Construct mock headers/metadata for the event, reflecting what InMemoryActorRuntime would do
                var eventHeaders = new Dictionary<string, string>(headers ?? new Dictionary<string, string>());
                if (senderId != null) eventHeaders["SenderId"] = senderId;
                eventHeaders["ReceiverId"] = targetActorId;
                if (!eventHeaders.ContainsKey("MessageType")) eventHeaders["MessageType"] = message.GetType().Name;

                MessageSent?.Invoke(this, new MessageSentEventArgs(
                    Guid.NewGuid().ToString(), 
                    senderId, 
                    targetActorId, 
                    eventHeaders["MessageType"] // MessageType from headers
                ));
                return Task.CompletedTask;
            }

            public Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout,
                string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
                where TResponse : class
            {
                if (ShouldThrowOnSendMessage)
                    throw new InvalidOperationException("Test exception during message send");

                SentMessages.Add((targetActorId, message, senderId, headers));
                
                var eventHeaders = new Dictionary<string, string>(headers ?? new Dictionary<string, string>());
                if (senderId != null) eventHeaders["SenderId"] = senderId;
                eventHeaders["ReceiverId"] = targetActorId;
                if (!eventHeaders.ContainsKey("MessageType")) eventHeaders["MessageType"] = message.GetType().Name;

                MessageSent?.Invoke(this, new MessageSentEventArgs(
                    Guid.NewGuid().ToString(), 
                    senderId, 
                    targetActorId, 
                    eventHeaders["MessageType"]
                ));

                // For TResponse that is IMessageEnvelope, create a mock response envelope
                if (typeof(TResponse) == typeof(IMessageEnvelope))
                {
                    var mockResponsePayload = "Mock response payload";
                    var responseEnvelopeHeaders = new Dictionary<string, string>
                    {
                        {"SenderId", targetActorId}, // Actor is sender
                        {"ReceiverId", senderId ?? "unknown"},
                        {"MessageType", "MockResponse"}
                    };
                    var responseEnvelopeMetadata = new Dictionary<string, object>
                    {
                        {"Timestamp", DateTimeOffset.UtcNow}
                    };
                    if (eventHeaders.TryGetValue("CorrelationId", out var cId)) responseEnvelopeMetadata["CorrelationId"] = cId;
                    
                    var mockResponse = new Mock<IMessageEnvelope>();
                    mockResponse.Setup(m => m.Id).Returns(Guid.NewGuid().ToString());
                    mockResponse.Setup(m => m.Payload).Returns(mockResponsePayload);
                    mockResponse.Setup(m => m.Headers).Returns(responseEnvelopeHeaders);
                    mockResponse.Setup(m => m.Metadata).Returns(responseEnvelopeMetadata);
                    return Task.FromResult(mockResponse.Object as TResponse)!;
                }

                return Task.FromResult(default(TResponse)!);
            }

            public Task StopActorAsync(string actorId, CancellationToken cancellationToken = default)
            {
                StoppedActorIds.Add(actorId);
                ActorStopped?.Invoke(this, new ActorStoppedEventArgs(actorId, "TestActor", "Stopped by test"));
                return Task.CompletedTask;
            }

            public Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default)
            {
                var activeIds = SpawnedActorIds.Except(StoppedActorIds);
                return Task.FromResult(activeIds);
            }

            public Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
            {
                var mockStats = new Mock<IRuntimeStatistics>();
                mockStats.Setup(s => s.ActiveActorCount).Returns(SpawnedActorIds.Count - StoppedActorIds.Count);
                mockStats.Setup(s => s.TotalMessagesProcessed).Returns(SentMessages.Count);
                mockStats.Setup(s => s.MessagesPerSecond).Returns(10.5);
                mockStats.Setup(s => s.AverageMessageProcessingTime).Returns(25.3);
                mockStats.Setup(s => s.Uptime).Returns(TimeSpan.FromMinutes(30));
                mockStats.Setup(s => s.MemoryUsageBytes).Returns(1024 * 1024);
                mockStats.Setup(s => s.AdditionalMetrics).Returns(new Dictionary<string, object>());
                return Task.FromResult(mockStats.Object);
            }

            public Task<string> RequestHumanInputAsync(string requestingAgentId, string prompt, string instructions, CancellationToken cancellationToken = default)
            {
                // For testing purposes, we can simulate different scenarios here if needed.
                // For now, return a default or configurable response, or throw if a test expects failure.
                if (prompt.Contains("throw_exception"))
                {
                    throw new InvalidOperationException("Test exception during human input");
                }
                return Task.FromResult("Default test human input");
            }

            public void Dispose()
            {
                IsInitialized = false;
            }
        }

        [TestMethod]
        public void RuntimeAdapter_ShouldHaveRequiredProperties()
        {
            // Arrange & Act
            var adapter = new TestActorRuntimeAdapter("TestRuntime", "2.1.0");

            // Assert
            Assert.AreEqual("TestRuntime", adapter.Name);
            Assert.AreEqual("2.1.0", adapter.Version);
            Assert.IsFalse(adapter.IsInitialized);
            Assert.IsNotNull(adapter.Configuration);
            Assert.AreEqual(0, adapter.Configuration.Count);
        }

        [TestMethod]
        public async Task InitializeAsync_ShouldSetInitializedState()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            var config = new Dictionary<string, object>
            {
                { "setting1", "value1" },
                { "setting2", 42 }
            };

            // Act
            await adapter.InitializeAsync(config);

            // Assert
            Assert.IsTrue(adapter.IsInitialized);
            Assert.AreEqual(2, adapter.Configuration.Count);
            Assert.AreEqual("value1", adapter.Configuration["setting1"]);
        }

        [TestMethod]
        public async Task InitializeAsync_ShouldThrowWhenExceptionOccurs()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter { ShouldThrowOnInitialize = true };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => adapter.InitializeAsync(new Dictionary<string, object>()));
        }
        
        [TestMethod]
        public async Task InitializeAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => adapter.InitializeAsync(new Dictionary<string, object>(), cts.Token));
        }

        [TestMethod]
        public async Task ShutdownAsync_ShouldClearInitializedState()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());

            // Act
            await adapter.ShutdownAsync();

            // Assert
            Assert.IsFalse(adapter.IsInitialized);
        }

        [TestMethod]
        public async Task SpawnActorAsync_ShouldCreateAndReturnActor()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            var actorId = "testActor1";

            // Act
            var actor = await adapter.SpawnActorAsync<TestActor>(actorId);

            // Assert
            Assert.IsNotNull(actor);
            Assert.AreEqual(actorId, actor.Id);
            Assert.AreEqual(1, adapter.SpawnedActorIds.Count);
            Assert.AreEqual(actorId, adapter.SpawnedActorIds[0]);
        }
        
        [TestMethod]
        public async Task SpawnActorAsync_ShouldHandleInitializationData()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            var actorId = "testActor2";
            var initData = new { Message = "Hello" };

            // Act
            var actor = await adapter.SpawnActorAsync<TestActor>(actorId, initData);

            // Assert
            Assert.IsNotNull(actor);
        }

        [TestMethod]
        public async Task GetActorAsync_ShouldReturnExistingActor()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            var actorId = "existingActor";
            await adapter.SpawnActorAsync<TestActor>(actorId);

            // Act
            var actor = await adapter.GetActorAsync<TestActor>(actorId);

            // Assert
            Assert.IsNotNull(actor);
            Assert.AreEqual(actorId, actor.Id);
        }

        [TestMethod]
        public async Task GetActorAsync_ShouldReturnNullForNonExistentActor()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();

            // Act
            var actor = await adapter.GetActorAsync<TestActor>("nonExistentActor");

            // Assert
            Assert.IsNull(actor);
        }
        
        [TestMethod]
        public async Task SendMessageAsync_ShouldSendMessage_MCP()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            var targetId = "targetActor";
            var message = new TestMessage { Content = "Hello, MCP!" };
            var senderId = "senderActor";
            var headers = new Dictionary<string, string> { { "CorrelationId", "corr-123" } };

            // Act
            await adapter.SendMessageAsync(targetId, message, senderId, headers);

            // Assert
            Assert.AreEqual(1, adapter.SentMessages.Count);
            var sent = adapter.SentMessages[0];
            Assert.AreEqual(targetId, sent.targetId);
            Assert.AreSame(message, sent.message);
            Assert.AreEqual(senderId, sent.senderId);
            Assert.AreEqual("corr-123", sent.headers["CorrelationId"]);
        }

        [TestMethod]
        public async Task SendMessageAsync_WithResponse_ShouldReturnResponse_MCP()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            var targetId = "targetActor";
            var message = new TestMessage { Content = "Request" };
            var senderId = "senderActor";
            var headers = new Dictionary<string, string> { { "CorrelationId", "corr-456" } };

            // Act
            var response = await adapter.SendMessageAsync<IMessageEnvelope>(targetId, message, TimeSpan.FromSeconds(5), senderId, headers);

            // Assert
            Assert.IsNotNull(response);
            Assert.AreEqual(1, adapter.SentMessages.Count);
            var sent = adapter.SentMessages[0];
            Assert.AreEqual(targetId, sent.targetId);
            
            // Verify the response from the mock
            Assert.AreEqual(targetId, response.Headers["SenderId"]);
            Assert.IsNotNull(response.Payload);
            Assert.AreEqual("Mock response payload", response.Payload as string);
        }

        [TestMethod]
        public async Task StopActorAsync_ShouldStopActor()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            var actorId = "actorToStop";
            await adapter.SpawnActorAsync<TestActor>(actorId);

            // Act
            await adapter.StopActorAsync(actorId);

            // Assert
            Assert.AreEqual(1, adapter.StoppedActorIds.Count);
            Assert.AreEqual(actorId, adapter.StoppedActorIds[0]);
        }
        
        [TestMethod]
        public async Task GetActiveActorIdsAsync_ShouldReturnActiveActors()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.SpawnActorAsync<TestActor>("actor1");
            await adapter.SpawnActorAsync<TestActor>("actor2");
            await adapter.StopActorAsync("actor1");

            // Act
            var activeIds = await adapter.GetActiveActorIdsAsync();

            // Assert
            Assert.AreEqual(1, activeIds.Count());
            Assert.AreEqual("actor2", activeIds.First());
        }

        [TestMethod]
        public async Task GetStatisticsAsync_ShouldReturnRuntimeStatistics()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.SpawnActorAsync<TestActor>("actor1");
            await adapter.SendMessageAsync("actor1", new TestMessage());

            // Act
            var stats = await adapter.GetStatisticsAsync();

            // Assert
            Assert.IsNotNull(stats);
            Assert.AreEqual(1, stats.ActiveActorCount);
            Assert.AreEqual(1, stats.TotalMessagesProcessed);
        }

        [TestMethod]
        public void Dispose_ShouldCleanupResources()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            adapter.InitializeAsync(new Dictionary<string, object>()).Wait();

            // Act
            adapter.Dispose();

            // Assert
            Assert.IsFalse(adapter.IsInitialized);
        }
        
        [TestMethod]
        public async Task RuntimeAdapter_ShouldHandleExceptionsDuringOperations()
        {
            // Initialize
            var adapter = new TestActorRuntimeAdapter { ShouldThrowOnInitialize = true };
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => adapter.InitializeAsync(new Dictionary<string, object>()));

            // Reset for next test
            adapter.ShouldThrowOnInitialize = false;
            await adapter.InitializeAsync(new Dictionary<string, object>());

            // Spawn
            adapter.ShouldThrowOnSpawnActor = true;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => adapter.SpawnActorAsync<TestActor>("failActor"));
            adapter.ShouldThrowOnSpawnActor = false;

            // Send Message
            adapter.ShouldThrowOnSendMessage = true;
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => adapter.SendMessageAsync("anyActor", "test"));
            adapter.ShouldThrowOnSendMessage = false;

            // Shutdown
            adapter.ShouldThrowOnShutdown = true;
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => adapter.ShutdownAsync());
        }
    }
    
    [TestClass]
    public class RuntimeAdapterEventArgsTests
    {
        [TestMethod]
        public void ActorSpawnedEventArgs_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var actorId = "spawnedActor";
            var actorType = "TestActor";

            // Act
            var args = new ActorSpawnedEventArgs(actorId, actorType);

            // Assert
            Assert.AreEqual(actorId, args.ActorId);
            Assert.AreEqual(actorType, args.ActorType);
            Assert.IsTrue(args.Timestamp <= DateTimeOffset.UtcNow);
        }

        [TestMethod]
        public void ActorStoppedEventArgs_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var actorId = "stoppedActor";
            var actorType = "TestActor";
            var reason = "Test cleanup";

            // Act
            var args = new ActorStoppedEventArgs(actorId, actorType, reason);

            // Assert
            Assert.AreEqual(actorId, args.ActorId);
            Assert.AreEqual(actorType, args.ActorType);
            Assert.AreEqual(reason, args.Reason);
            Assert.IsTrue(args.Timestamp <= DateTimeOffset.UtcNow);
        }

        [TestMethod]
        public void MessageSentEventArgs_ShouldSetPropertiesCorrectly_MCP()
        {
            // Arrange
            var messageId = "msg-987";
            var senderId = "sender-007";
            var receiverId = "receiver-008";
            var messageType = "CommandMessage";

            // Act
            var args = new MessageSentEventArgs(messageId, senderId, receiverId, messageType);

            // Assert
            Assert.AreEqual(messageId, args.MessageId);
            Assert.AreEqual(senderId, args.SenderId);
            Assert.AreEqual(receiverId, args.ReceiverId);
            Assert.AreEqual(messageType, args.MessageType);
        }
    }

    // Helper classes for tests
    public class TestActor : IActor
    {
        public string Id { get; set; }
        public string ActorType => nameof(TestActor);
        public ActorState State { get; private set; } = ActorState.Initializing;

        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

        public TestActor(string id) { Id = id; }
        public TestActor() { Id = string.Empty; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            State = ActorState.Active;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(ActorState.Initializing, ActorState.Active));
            return Task.CompletedTask;
        }

        public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope message, CancellationToken cancellationToken = default)
        {
            // Dummy implementation for testing
            return Task.FromResult(message);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            State = ActorState.Stopped;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(ActorState.Active, ActorState.Stopped));
            return Task.CompletedTask;
        }
    }

    public class TestMessage
    {
        public string Content { get; set; } = string.Empty;
    }
} 