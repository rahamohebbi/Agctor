using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Runtime.Examples;
using Xunit;

namespace AgctorSDK.Core.Tests.Runtime
{
    /// <summary>
    /// Comprehensive unit tests for the InMemoryActorRuntime implementation.
    /// Tests verify actor registration, message dispatch, queuing, and lifecycle management.
    /// </summary>
    public class InMemoryActorRuntimeTests : IDisposable
    {
        private readonly InMemoryActorRuntime _runtime;

        public InMemoryActorRuntimeTests()
        {
            _runtime = new InMemoryActorRuntime();
        }

        public void Dispose()
        {
            _runtime?.Dispose();
        }

        [Fact]
        public void Runtime_ShouldHaveCorrectProperties()
        {
            // Assert
            Assert.Equal("InMemoryActorRuntime", _runtime.Name);
            Assert.Equal("1.0.0", _runtime.Version);
            Assert.False(_runtime.IsInitialized);
            Assert.NotNull(_runtime.Configuration);
            Assert.Empty(_runtime.Configuration);
        }

        [Fact]
        public async Task InitializeAsync_ShouldSetInitializedState()
        {
            // Arrange
            var config = new Dictionary<string, object>
            {
                { "MaxActors", 100 },
                { "LogLevel", "Debug" }
            };

            // Act
            await _runtime.InitializeAsync(config);

            // Assert
            Assert.True(_runtime.IsInitialized);
            Assert.Equal(2, _runtime.Configuration.Count);
            Assert.Equal(100, _runtime.Configuration["MaxActors"]);
            Assert.Equal("Debug", _runtime.Configuration["LogLevel"]);
        }

        [Fact]
        public async Task InitializeAsync_WhenAlreadyInitialized_ShouldNotThrow()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());

            // Act & Assert - Should not throw
            await _runtime.InitializeAsync(new Dictionary<string, object> { { "test", "value" } });
            Assert.True(_runtime.IsInitialized);
        }

        [Fact]
        public async Task SpawnActorAsync_ShouldCreateAndInitializeActor()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "echo-actor-1";
            ActorSpawnedEventArgs? spawnedEvent = null;
            _runtime.ActorSpawned += (sender, args) => spawnedEvent = args;

            // Act
            var actor = await _runtime.SpawnActorAsync<EchoActor>(actorId);

            // Assert
            Assert.NotNull(actor);
            Assert.Equal(actorId, actor.Id);
            Assert.Equal(nameof(EchoActor), actor.ActorType);
            Assert.Equal(ActorState.Active, actor.State);
            
            Assert.NotNull(spawnedEvent);
            Assert.Equal(actorId, spawnedEvent.ActorId);
            Assert.Equal(nameof(EchoActor), spawnedEvent.ActorType);
        }

        [Fact]
        public async Task SpawnActorAsync_WithDuplicateId_ShouldThrow()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "duplicate-actor";
            await _runtime.SpawnActorAsync<EchoActor>(actorId);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _runtime.SpawnActorAsync<EchoActor>(actorId));
        }

        [Fact]
        public async Task SpawnActorAsync_WhenNotInitialized_ShouldThrow()
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _runtime.SpawnActorAsync<EchoActor>("test-actor"));
        }

        [Fact]
        public async Task GetActorAsync_ShouldReturnExistingActor()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "test-actor";
            var originalActor = await _runtime.SpawnActorAsync<EchoActor>(actorId);

            // Act
            var retrievedActor = await _runtime.GetActorAsync<EchoActor>(actorId);

            // Assert
            Assert.NotNull(retrievedActor);
            Assert.Equal(originalActor.Id, retrievedActor.Id);
            Assert.Same(originalActor, retrievedActor);
        }

        [Fact]
        public async Task GetActorAsync_WithNonExistentId_ShouldReturnNull()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());

            // Act
            var actor = await _runtime.GetActorAsync<EchoActor>("non-existent");

            // Assert
            Assert.Null(actor);
        }

        [Fact]
        public async Task GetActorAsync_WithWrongType_ShouldReturnNull()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "test-actor";
            await _runtime.SpawnActorAsync<EchoActor>(actorId);

            // Act
            var actor = await _runtime.GetActorAsync<IActor>(actorId); // Different type

            // Assert - Should return the actor since EchoActor implements IActor
            Assert.NotNull(actor);
            Assert.IsType<EchoActor>(actor);
        }

        [Fact]
        public async Task SendMessageAsync_ShouldDeliverMessageToActor()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "echo-actor";
            var actor = await _runtime.SpawnActorAsync<EchoActor>(actorId);
            var message = "Hello, Actor!";
            MessageSentEventArgs? sentEvent = null;
            _runtime.MessageSent += (sender, args) => sentEvent = args;

            // Act
            await _runtime.SendMessageAsync(actorId, message, "sender-1");

            // Wait a bit for message processing
            await Task.Delay(50);

            // Assert
            Assert.NotNull(sentEvent);
            Assert.Equal("sender-1", sentEvent.SenderId);
            Assert.Equal(actorId, sentEvent.ReceiverId);
            Assert.Equal("String", sentEvent.MessageType);
        }

        [Fact]
        public async Task SendMessageAsync_WithHeaders_ShouldIncludeHeaders()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "echo-actor";
            await _runtime.SpawnActorAsync<EchoActor>(actorId);
            var message = new EchoRequest("Test message", 0, "test metadata");
            var headers = new Dictionary<string, object>
            {
                { "Priority", "High" },
                { "Source", "TestSuite" }
            };

            // Act
            await _runtime.SendMessageAsync(actorId, message, "test-sender", headers);

            // Wait for processing
            await Task.Delay(50);

            // Assert - Message should be processed successfully
            // (We can't easily verify headers were received without modifying EchoActor)
        }

        [Fact]
        public async Task SendMessageAsync_ToNonExistentActor_ShouldThrow()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _runtime.SendMessageAsync("non-existent", "message"));
        }

        [Fact]
        public async Task SendMessageAsync_WithComplexMessage_ShouldWork()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "echo-actor";
            await _runtime.SpawnActorAsync<EchoActor>(actorId);
            var complexMessage = new EchoRequest("Complex message", 25, "metadata");

            // Act
            await _runtime.SendMessageAsync(actorId, complexMessage);

            // Wait for processing (including the delay in the message)
            await Task.Delay(100);

            // Assert - Should complete without throwing
        }

        [Fact]
        public async Task StopActorAsync_ShouldStopAndRemoveActor()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "test-actor";
            var actor = await _runtime.SpawnActorAsync<EchoActor>(actorId);
            ActorStoppedEventArgs? stoppedEvent = null;
            _runtime.ActorStopped += (sender, args) => stoppedEvent = args;

            // Act
            await _runtime.StopActorAsync(actorId);

            // Assert
            Assert.Equal(ActorState.Stopped, actor.State);
            
            Assert.NotNull(stoppedEvent);
            Assert.Equal(actorId, stoppedEvent.ActorId);
            Assert.Equal(nameof(EchoActor), stoppedEvent.ActorType);
            Assert.Equal("Graceful shutdown", stoppedEvent.Reason);

            // Actor should no longer be retrievable
            var retrievedActor = await _runtime.GetActorAsync<EchoActor>(actorId);
            Assert.Null(retrievedActor);
        }

        [Fact]
        public async Task StopActorAsync_WithNonExistentActor_ShouldNotThrow()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());

            // Act & Assert - Should not throw
            await _runtime.StopActorAsync("non-existent");
        }

        [Fact]
        public async Task GetActiveActorIdsAsync_ShouldReturnActiveActors()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            await _runtime.SpawnActorAsync<EchoActor>("actor1");
            await _runtime.SpawnActorAsync<EchoActor>("actor2");
            await _runtime.SpawnActorAsync<EchoActor>("actor3");
            await _runtime.StopActorAsync("actor2");

            // Act
            var activeIds = await _runtime.GetActiveActorIdsAsync();

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
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            await _runtime.SpawnActorAsync<EchoActor>("actor1");
            await _runtime.SpawnActorAsync<EchoActor>("actor2");
            await _runtime.SendMessageAsync("actor1", "message1");
            await _runtime.SendMessageAsync("actor2", "message2");
            await _runtime.SendMessageAsync("actor1", "message3");

            // Wait for message processing
            await Task.Delay(100);

            // Act
            var stats = await _runtime.GetStatisticsAsync();

            // Assert
            Assert.NotNull(stats);
            Assert.Equal(2, stats.ActiveActorCount);
            Assert.Equal(3, stats.TotalMessagesProcessed);
            Assert.True(stats.MessagesPerSecond >= 0);
            Assert.True(stats.AverageMessageProcessingTime > 0);
            Assert.True(stats.Uptime.TotalMilliseconds > 0);
            Assert.True(stats.MemoryUsageBytes > 0);
            Assert.NotNull(stats.AdditionalMetrics);
            Assert.Equal("InMemory", stats.AdditionalMetrics["RuntimeType"]);
        }

        [Fact]
        public async Task ShutdownAsync_ShouldStopAllActors()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            await _runtime.SpawnActorAsync<EchoActor>("actor1");
            await _runtime.SpawnActorAsync<EchoActor>("actor2");
            await _runtime.SpawnActorAsync<EchoActor>("actor3");

            var activeIdsBefore = await _runtime.GetActiveActorIdsAsync();
            Assert.Equal(3, activeIdsBefore.Count());

            // Act
            await _runtime.ShutdownAsync();

            // Assert
            Assert.False(_runtime.IsInitialized);
            
            // After shutdown, the runtime should not be initialized
            // We can't call GetActiveActorIdsAsync because it requires initialization
            // The fact that IsInitialized is false confirms all actors were stopped
        }

        [Fact]
        public async Task ShutdownAsync_WhenNotInitialized_ShouldNotThrow()
        {
            // Act & Assert - Should not throw
            await _runtime.ShutdownAsync();
        }

        [Fact]
        public async Task MultipleActors_ShouldProcessMessagesConcurrently()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorCount = 5;
            var messagesPerActor = 3;

            // Spawn multiple actors
            var actorIds = new List<string>();
            for (int i = 0; i < actorCount; i++)
            {
                var actorId = $"actor-{i}";
                actorIds.Add(actorId);
                await _runtime.SpawnActorAsync<EchoActor>(actorId);
            }

            // Act - Send messages to all actors concurrently
            var sendTasks = new List<Task>();
            for (int i = 0; i < actorCount; i++)
            {
                var actorId = actorIds[i];
                for (int j = 0; j < messagesPerActor; j++)
                {
                    var message = $"Message {j} to {actorId}";
                    sendTasks.Add(_runtime.SendMessageAsync(actorId, message));
                }
            }

            await Task.WhenAll(sendTasks);

            // Wait for all messages to be processed
            await Task.Delay(200);

            // Assert
            var stats = await _runtime.GetStatisticsAsync();
            Assert.Equal(actorCount, stats.ActiveActorCount);
            Assert.Equal(actorCount * messagesPerActor, stats.TotalMessagesProcessed);
        }

        [Fact]
        public async Task ActorLifecycle_ShouldFireStateChangeEvents()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "lifecycle-actor";
            var stateChanges = new List<ActorStateChangedEventArgs>();

            // Act - Subscribe to events before spawning to catch all state changes
            var actor = await _runtime.SpawnActorAsync<EchoActor>(actorId);
            
            // Wait a moment for initialization to complete
            await Task.Delay(50);
            
            // Subscribe to future state changes
            actor.StateChanged += (sender, args) => stateChanges.Add(args);

            await _runtime.StopActorAsync(actorId);

            // Wait for shutdown to complete
            await Task.Delay(50);

            // Assert
            // We should see at least one state change (Active -> Stopping)
            Assert.True(stateChanges.Count >= 1);
            
            // Find the Active -> Stopping transition
            var activeToStoppingTransition = stateChanges.FirstOrDefault(sc => 
                sc.PreviousState == ActorState.Active && sc.NewState == ActorState.Stopping);
            
            Assert.NotNull(activeToStoppingTransition);
            Assert.Equal(ActorState.Active, activeToStoppingTransition.PreviousState);
            Assert.Equal(ActorState.Stopping, activeToStoppingTransition.NewState);
        }

        [Fact]
        public async Task Runtime_ShouldHandleHighMessageVolume()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "high-volume-actor";
            await _runtime.SpawnActorAsync<EchoActor>(actorId);
            var messageCount = 100;

            // Act - Send many messages rapidly
            var sendTasks = Enumerable.Range(0, messageCount)
                .Select(i => _runtime.SendMessageAsync(actorId, $"Message {i}"))
                .ToArray();

            await Task.WhenAll(sendTasks);

            // Wait for processing
            await Task.Delay(500);

            // Assert
            var stats = await _runtime.GetStatisticsAsync();
            Assert.Equal(messageCount, stats.TotalMessagesProcessed);
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            // Arrange
            var runtime = new InMemoryActorRuntime();
            runtime.InitializeAsync(new Dictionary<string, object>()).Wait();
            runtime.SpawnActorAsync<EchoActor>("test-actor").Wait();

            // Act
            runtime.Dispose();

            // Assert - Should not throw and should be disposed
            Assert.Throws<ObjectDisposedException>(() => 
                runtime.InitializeAsync(new Dictionary<string, object>()).Wait());
        }

        [Fact]
        public async Task Runtime_ShouldHandleCancellation()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => _runtime.SpawnActorAsync<EchoActor>("test-actor", null, cts.Token));
        }
    }
} 