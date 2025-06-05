using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using Moq;
using Xunit;

namespace AgctorSDK.Core.Tests.Interfaces
{
    /// <summary>
    /// Unit tests for the IActor interface contract and behavior.
    /// Tests verify that actor implementations properly handle lifecycle and message processing.
    /// </summary>
    public class IActorTests
    {
        /// <summary>
        /// Test actor implementation for testing purposes.
        /// Implements the IActor interface with controllable behavior for testing.
        /// </summary>
        private class TestActor : IActor
        {
            public string Id { get; }
            public string ActorType { get; }
            public ActorState State { get; private set; }

            public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

            // Test control properties
            public bool InitializeCalled { get; private set; }
            public bool ShutdownCalled { get; private set; }
            public IMessageEnvelope? LastReceivedMessage { get; private set; }
            public bool ShouldThrowOnReceive { get; set; }
            public bool ShouldThrowOnInitialize { get; set; }
            public bool ShouldThrowOnShutdown { get; set; }

            public TestActor(string id, string actorType = "TestActor")
            {
                Id = id;
                ActorType = actorType;
                State = ActorState.Initializing;
            }

            public Task InitializeAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (ShouldThrowOnInitialize)
                    throw new InvalidOperationException("Test exception during initialization");

                InitializeCalled = true;
                ChangeState(ActorState.Active, "Initialized successfully");
                return Task.CompletedTask;
            }

            public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (ShouldThrowOnReceive)
                    throw new InvalidOperationException("Test exception during message processing");

                LastReceivedMessage = envelope;
                return Task.FromResult<IMessageEnvelope>(envelope);
            }

            public Task ShutdownAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (ShouldThrowOnShutdown)
                    throw new InvalidOperationException("Test exception during shutdown");

                ShutdownCalled = true;
                ChangeState(ActorState.Stopped, "Shutdown completed");
                return Task.CompletedTask;
            }

            private void ChangeState(ActorState newState, string? reason = null)
            {
                var previousState = State;
                State = newState;
                StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previousState, newState, reason));
            }
        }

        [Fact]
        public void Actor_ShouldHaveRequiredProperties()
        {
            // Arrange & Act
            var actor = new TestActor("test-actor-1", "TestActorType");

            // Assert
            Assert.Equal("test-actor-1", actor.Id);
            Assert.Equal("TestActorType", actor.ActorType);
            Assert.Equal(ActorState.Initializing, actor.State);
        }

        [Fact]
        public async Task InitializeAsync_ShouldChangeStateToActive()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            ActorStateChangedEventArgs? stateChangeArgs = null;
            actor.StateChanged += (sender, args) => stateChangeArgs = args;

            // Act
            await actor.InitializeAsync();

            // Assert
            Assert.True(actor.InitializeCalled);
            Assert.Equal(ActorState.Active, actor.State);
            Assert.NotNull(stateChangeArgs);
            Assert.Equal(ActorState.Initializing, stateChangeArgs.PreviousState);
            Assert.Equal(ActorState.Active, stateChangeArgs.NewState);
            Assert.Equal("Initialized successfully", stateChangeArgs.Reason);
        }

        [Fact]
        public async Task InitializeAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => actor.InitializeAsync(cts.Token));
        }

        [Fact]
        public async Task ReceiveAsync_ShouldProcessMessageEnvelope()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            var mockEnvelope = new Mock<IMessageEnvelope>();
            mockEnvelope.Setup(e => e.Id).Returns("msg-123");
            mockEnvelope.Setup(e => e.Payload).Returns("test message");

            // Act
            await actor.ReceiveAsync(mockEnvelope.Object);

            // Assert
            Assert.Equal(mockEnvelope.Object, actor.LastReceivedMessage);
        }

        [Fact]
        public async Task ReceiveAsync_ShouldHandleExceptions()
        {
            // Arrange
            var actor = new TestActor("test-actor-1") { ShouldThrowOnReceive = true };
            var mockEnvelope = new Mock<IMessageEnvelope>();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => actor.ReceiveAsync(mockEnvelope.Object));
        }

        [Fact]
        public async Task ShutdownAsync_ShouldChangeStateToStopped()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            await actor.InitializeAsync(); // Initialize first
            ActorStateChangedEventArgs? stateChangeArgs = null;
            actor.StateChanged += (sender, args) => stateChangeArgs = args;

            // Act
            await actor.ShutdownAsync();

            // Assert
            Assert.True(actor.ShutdownCalled);
            Assert.Equal(ActorState.Stopped, actor.State);
            Assert.NotNull(stateChangeArgs);
            Assert.Equal(ActorState.Active, stateChangeArgs.PreviousState);
            Assert.Equal(ActorState.Stopped, stateChangeArgs.NewState);
            Assert.Equal("Shutdown completed", stateChangeArgs.Reason);
        }

        [Fact]
        public async Task ShutdownAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => actor.ShutdownAsync(cts.Token));
        }

        [Fact]
        public async Task StateChanged_ShouldFireWhenStateChanges()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            var stateChanges = new List<ActorStateChangedEventArgs>();
            actor.StateChanged += (sender, args) => stateChanges.Add(args);

            // Act
            await actor.InitializeAsync();
            await actor.ShutdownAsync();

            // Assert
            Assert.Equal(2, stateChanges.Count);
            
            // First state change: Initializing -> Active
            Assert.Equal(ActorState.Initializing, stateChanges[0].PreviousState);
            Assert.Equal(ActorState.Active, stateChanges[0].NewState);
            
            // Second state change: Active -> Stopped
            Assert.Equal(ActorState.Active, stateChanges[1].PreviousState);
            Assert.Equal(ActorState.Stopped, stateChanges[1].NewState);
        }

        [Theory]
        [InlineData(ActorState.Initializing)]
        [InlineData(ActorState.Active)]
        [InlineData(ActorState.Inactive)]
        [InlineData(ActorState.Stopping)]
        [InlineData(ActorState.Stopped)]
        [InlineData(ActorState.Faulted)]
        public void ActorState_ShouldSupportAllDefinedStates(ActorState expectedState)
        {
            // This test verifies that all enum values are properly defined
            // and can be used in actor implementations
            Assert.True(Enum.IsDefined(typeof(ActorState), expectedState));
        }
    }

    /// <summary>
    /// Unit tests for the ActorStateChangedEventArgs class.
    /// Tests verify proper event argument construction and properties.
    /// </summary>
    public class ActorStateChangedEventArgsTests
    {
        [Fact]
        public void Constructor_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var previousState = ActorState.Initializing;
            var newState = ActorState.Active;
            var reason = "Test state change";
            var beforeTimestamp = DateTimeOffset.UtcNow;

            // Act
            var eventArgs = new ActorStateChangedEventArgs(previousState, newState, reason);
            var afterTimestamp = DateTimeOffset.UtcNow;

            // Assert
            Assert.Equal(previousState, eventArgs.PreviousState);
            Assert.Equal(newState, eventArgs.NewState);
            Assert.Equal(reason, eventArgs.Reason);
            Assert.True(eventArgs.Timestamp >= beforeTimestamp);
            Assert.True(eventArgs.Timestamp <= afterTimestamp);
        }

        [Fact]
        public void Constructor_ShouldHandleNullReason()
        {
            // Arrange & Act
            var eventArgs = new ActorStateChangedEventArgs(ActorState.Active, ActorState.Stopped);

            // Assert
            Assert.Null(eventArgs.Reason);
            Assert.Equal(ActorState.Active, eventArgs.PreviousState);
            Assert.Equal(ActorState.Stopped, eventArgs.NewState);
        }

        [Fact]
        public void Timestamp_ShouldBeSetToCurrentUtcTime()
        {
            // Arrange
            var beforeCreation = DateTimeOffset.UtcNow;

            // Act
            var eventArgs = new ActorStateChangedEventArgs(ActorState.Active, ActorState.Stopped);
            var afterCreation = DateTimeOffset.UtcNow;

            // Assert
            Assert.True(eventArgs.Timestamp >= beforeCreation);
            Assert.True(eventArgs.Timestamp <= afterCreation);
            // DateTimeOffset preserves timezone information, so we check the offset instead
            Assert.Equal(TimeSpan.Zero, eventArgs.Timestamp.Offset);
        }
    }
} 