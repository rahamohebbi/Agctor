using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Events;
using Moq;
using Xunit;

namespace AgctorSDK.Core.Tests.Interfaces
{
    /// <summary>
    /// Unit tests for the IActorRuntimeAdapter interface contract and behavior.
    /// Tests verify that runtime adapter implementations properly handle actor lifecycle and messaging.
    /// </summary>
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
                if (ShouldThrowOnSpawnActor)
                    throw new InvalidOperationException("Test exception during actor spawn");

                SpawnedActorIds.Add(actorId);
                
                // Create a mock actor for testing
                var mockActor = new Mock<T>();
                if (mockActor.Object is IActor actor)
                {
                    var actorMock = mockActor.As<IActor>();
                    actorMock.Setup(a => a.Id).Returns(actorId);
                    actorMock.Setup(a => a.ActorType).Returns(typeof(T).Name);
                    actorMock.Setup(a => a.State).Returns(ActorState.Active);
                }

                ActorSpawned?.Invoke(this, new ActorSpawnedEventArgs(actorId, typeof(T).Name));
                return Task.FromResult(mockActor.Object);
            }

            public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor
            {
                if (SpawnedActorIds.Contains(actorId) && !StoppedActorIds.Contains(actorId))
                {
                    var mockActor = new Mock<T>();
                    if (mockActor.Object is IActor actor)
                    {
                        var actorMock = mockActor.As<IActor>();
                        actorMock.Setup(a => a.Id).Returns(actorId);
                        actorMock.Setup(a => a.ActorType).Returns(typeof(T).Name);
                        actorMock.Setup(a => a.State).Returns(ActorState.Active);
                    }
                    return Task.FromResult<T?>(mockActor.Object);
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

        [Fact]
        public void RuntimeAdapter_ShouldHaveRequiredProperties()
        {
            // Arrange & Act
            var adapter = new TestActorRuntimeAdapter("TestRuntime", "2.1.0");

            // Assert
            Assert.Equal("TestRuntime", adapter.Name);
            Assert.Equal("2.1.0", adapter.Version);
            Assert.False(adapter.IsInitialized);
            Assert.NotNull(adapter.Configuration);
            Assert.Empty(adapter.Configuration);
        }

        [Fact]
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
            Assert.True(adapter.IsInitialized);
            Assert.Equal(2, adapter.Configuration.Count);
            Assert.Equal("value1", adapter.Configuration["setting1"]);
            Assert.Equal(42, adapter.Configuration["setting2"]);
        }

        [Fact]
        public async Task InitializeAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => adapter.InitializeAsync(new Dictionary<string, object>(), cts.Token));
        }

        [Fact]
        public async Task ShutdownAsync_ShouldClearInitializedState()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            Assert.True(adapter.IsInitialized);

            // Act
            await adapter.ShutdownAsync();

            // Assert
            Assert.False(adapter.IsInitialized);
        }

        [Fact]
        public async Task SpawnActorAsync_ShouldCreateAndReturnActor()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            var actorId = "test-actor-123";
            ActorSpawnedEventArgs? spawnedEvent = null;
            adapter.ActorSpawned += (sender, args) => spawnedEvent = args;

            // Act
            var actor = await adapter.SpawnActorAsync<IActor>(actorId);

            // Assert
            Assert.NotNull(actor);
            Assert.Equal(actorId, actor.Id);
            Assert.Contains(actorId, adapter.SpawnedActorIds);
            Assert.NotNull(spawnedEvent);
            Assert.Equal(actorId, spawnedEvent.ActorId);
            Assert.Equal("IActor", spawnedEvent.ActorType);
        }

        [Fact]
        public async Task SpawnActorAsync_ShouldHandleInitializationData()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            var actorId = "test-actor-123";
            var initData = new { Setting = "value", Number = 42 };

            // Act
            var actor = await adapter.SpawnActorAsync<IActor>(actorId, initData);

            // Assert
            Assert.NotNull(actor);
            Assert.Equal(actorId, actor.Id);
        }

        [Fact]
        public async Task GetActorAsync_ShouldReturnExistingActor()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            var actorId = "test-actor-123";
            await adapter.SpawnActorAsync<IActor>(actorId);

            // Act
            var actor = await adapter.GetActorAsync<IActor>(actorId);

            // Assert
            Assert.NotNull(actor);
            Assert.Equal(actorId, actor.Id);
        }

        [Fact]
        public async Task GetActorAsync_ShouldReturnNullForNonExistentActor()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());

            // Act
            var actor = await adapter.GetActorAsync<IActor>("non-existent-actor");

            // Assert
            Assert.Null(actor);
        }

        [Fact]
        public async Task SendMessageAsync_ShouldSendMessage_MCP()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            var targetId = "actor-1";
            var message = "Hello";
            var senderId = "sender-A";
            var headers = new Dictionary<string, string> { { "CustomHeader", "CustomValue" }, { "CorrelationId", "corr-1" } };
            MessageSentEventArgs? receivedEvent = null;
            adapter.MessageSent += (s, e) => receivedEvent = e;

            // Act
            await adapter.SendMessageAsync(targetId, message, senderId, headers);

            // Assert
            Assert.Single(adapter.SentMessages);
            var sentMsg = adapter.SentMessages.First();
            Assert.Equal(targetId, sentMsg.targetId);
            Assert.Equal(message, sentMsg.message);
            Assert.Equal(senderId, sentMsg.senderId);
            Assert.Equal(headers, sentMsg.headers);

            Assert.NotNull(receivedEvent);
            Assert.Equal(senderId, receivedEvent.SenderId);
            Assert.Equal(targetId, receivedEvent.ReceiverId);
            Assert.Equal("String", receivedEvent.MessageType); // Default from payload type
        }

        [Fact]
        public async Task SendMessageAsync_WithResponse_ShouldReturnResponse_MCP()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            var targetId = "actor-B";
            var message = "Request for data";
            var senderId = "sender-C";
            var headers = new Dictionary<string, string> { { "Priority", "High" }, { "CorrelationId", "corr-2" } };

            // Act
            var response = await adapter.SendMessageAsync<IMessageEnvelope>(targetId, message, TimeSpan.FromSeconds(5), senderId, headers);

            // Assert
            Assert.Single(adapter.SentMessages);
            var sentMsg = adapter.SentMessages.First();
            Assert.Equal(targetId, sentMsg.targetId);
            Assert.Equal(message, sentMsg.message);
            Assert.Equal(senderId, sentMsg.senderId);
            Assert.Equal(headers, sentMsg.headers);
            
            Assert.NotNull(response);
            Assert.IsAssignableFrom<IMessageEnvelope>(response);
            Assert.Equal("MockResponse", response.Headers["MessageType"]);
            Assert.Equal(targetId, response.Headers["SenderId"]); // Actor is sender of response
            Assert.Equal(senderId, response.Headers["ReceiverId"]);
            Assert.Equal("corr-2", response.Metadata["CorrelationId"]); // Echoed CorrelationId
        }

        [Fact]
        public async Task StopActorAsync_ShouldStopActor()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            var actorId = "test-actor-123";
            await adapter.SpawnActorAsync<IActor>(actorId);
            ActorStoppedEventArgs? stoppedEvent = null;
            adapter.ActorStopped += (sender, args) => stoppedEvent = args;

            // Act
            await adapter.StopActorAsync(actorId);

            // Assert
            Assert.Contains(actorId, adapter.StoppedActorIds);
            Assert.NotNull(stoppedEvent);
            Assert.Equal(actorId, stoppedEvent.ActorId);
            Assert.Equal("TestActor", stoppedEvent.ActorType);
            Assert.Equal("Stopped by test", stoppedEvent.Reason);
        }

        [Fact]
        public async Task GetActiveActorIdsAsync_ShouldReturnActiveActors()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            await adapter.SpawnActorAsync<IActor>("actor1");
            await adapter.SpawnActorAsync<IActor>("actor2");
            await adapter.SpawnActorAsync<IActor>("actor3");
            await adapter.StopActorAsync("actor2");

            // Act
            var activeIds = await adapter.GetActiveActorIdsAsync();

            // Assert
            var activeList = activeIds.ToList();
            Assert.Equal(2, activeList.Count);
            Assert.Contains("actor1", activeList);
            Assert.Contains("actor3", activeList);
            Assert.DoesNotContain("actor2", activeList);
        }

        [Fact]
        public async Task GetStatisticsAsync_ShouldReturnRuntimeStatistics()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            await adapter.SpawnActorAsync<IActor>("actor1");
            await adapter.SpawnActorAsync<IActor>("actor2");
            await adapter.SendMessageAsync("actor1", "message1");
            await adapter.SendMessageAsync("actor2", "message2");

            // Act
            var stats = await adapter.GetStatisticsAsync();

            // Assert
            Assert.NotNull(stats);
            Assert.Equal(2, stats.ActiveActorCount);
            Assert.Equal(2, stats.TotalMessagesProcessed);
            Assert.Equal(10.5, stats.MessagesPerSecond);
            Assert.Equal(25.3, stats.AverageMessageProcessingTime);
            Assert.Equal(TimeSpan.FromMinutes(30), stats.Uptime);
            Assert.Equal(1024 * 1024, stats.MemoryUsageBytes);
            Assert.NotNull(stats.AdditionalMetrics);
        }

        [Fact]
        public async Task Dispose_ShouldCleanupResources()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter();
            await adapter.InitializeAsync(new Dictionary<string, object>());
            Assert.True(adapter.IsInitialized);

            // Act
            adapter.Dispose();

            // Assert
            Assert.False(adapter.IsInitialized);
        }

        [Fact]
        public async Task RuntimeAdapter_ShouldHandleExceptionsDuringOperations()
        {
            // Arrange
            var adapter = new TestActorRuntimeAdapter
            {
                ShouldThrowOnInitialize = true,
                ShouldThrowOnShutdown = true,
                ShouldThrowOnSpawnActor = true,
                ShouldThrowOnSendMessage = true
            };

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.InitializeAsync(new Dictionary<string, object>()));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.ShutdownAsync());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.SpawnActorAsync<IActor>("test-actor"));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.SendMessageAsync("target", "message"));
        }
    }

    /// <summary>
    /// Unit tests for the event argument classes used by IActorRuntimeAdapter.
    /// </summary>
    public class RuntimeAdapterEventArgsTests
    {
        [Fact]
        public void ActorSpawnedEventArgs_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var actorId = "test-actor-123";
            var actorType = "TestActor";
            var beforeTimestamp = DateTimeOffset.UtcNow;

            // Act
            var eventArgs = new ActorSpawnedEventArgs(actorId, actorType);
            var afterTimestamp = DateTimeOffset.UtcNow;

            // Assert
            Assert.Equal(actorId, eventArgs.ActorId);
            Assert.Equal(actorType, eventArgs.ActorType);
            Assert.True(eventArgs.Timestamp >= beforeTimestamp);
            Assert.True(eventArgs.Timestamp <= afterTimestamp);
        }

        [Fact]
        public void ActorStoppedEventArgs_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var actorId = "test-actor-123";
            var actorType = "TestActor";
            var reason = "Graceful shutdown";
            var beforeTimestamp = DateTimeOffset.UtcNow;

            // Act
            var eventArgs = new ActorStoppedEventArgs(actorId, actorType, reason);
            var afterTimestamp = DateTimeOffset.UtcNow;

            // Assert
            Assert.Equal(actorId, eventArgs.ActorId);
            Assert.Equal(actorType, eventArgs.ActorType);
            Assert.Equal(reason, eventArgs.Reason);
            Assert.True(eventArgs.Timestamp >= beforeTimestamp);
            Assert.True(eventArgs.Timestamp <= afterTimestamp);
        }

        [Fact]
        public void MessageSentEventArgs_ShouldSetPropertiesCorrectly_MCP()
        {
            // Arrange
            var messageId = "msg-789";
            var senderId = "sender-X";
            var receiverId = "receiver-Y";
            var messageType = "TestEvent"; // This is the value for MessageType header

            // Act
            var args = new MessageSentEventArgs(messageId, senderId, receiverId, messageType);

            // Assert
            Assert.Equal(messageId, args.MessageId);
            Assert.Equal(senderId, args.SenderId);
            Assert.Equal(receiverId, args.ReceiverId);
            Assert.Equal(messageType, args.MessageType); // Convenience property
            Assert.True(args.Timestamp <= DateTimeOffset.UtcNow && args.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-1));
        }
    }
} 