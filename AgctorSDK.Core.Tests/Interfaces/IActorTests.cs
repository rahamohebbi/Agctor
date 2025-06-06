using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AgctorSDK.Core.Tests.Interfaces
{
    /// <summary>
    /// Unit tests for the IActor interface contract and behavior.
    /// Tests verify that actor implementations properly handle lifecycle and message processing.
    /// </summary>
    [TestClass]
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

        [TestMethod]
        public void Actor_ShouldHaveRequiredProperties()
        {
            // Arrange & Act
            var actor = new TestActor("test-actor-1", "TestActorType");

            // Assert
            Assert.AreEqual("test-actor-1", actor.Id);
            Assert.AreEqual("TestActorType", actor.ActorType);
            Assert.AreEqual(ActorState.Initializing, actor.State);
        }

        [TestMethod]
        public async Task InitializeAsync_ShouldChangeStateToActive()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            ActorStateChangedEventArgs? stateChangeArgs = null;
            actor.StateChanged += (sender, args) => stateChangeArgs = args;

            // Act
            await actor.InitializeAsync();

            // Assert
            Assert.IsTrue(actor.InitializeCalled);
            Assert.AreEqual(ActorState.Active, actor.State);
            Assert.IsNotNull(stateChangeArgs);
            Assert.AreEqual(ActorState.Initializing, stateChangeArgs.PreviousState);
            Assert.AreEqual(ActorState.Active, stateChangeArgs.NewState);
            Assert.AreEqual("Initialized successfully", stateChangeArgs.Reason);
        }

        [TestMethod]
        public async Task InitializeAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => actor.InitializeAsync(cts.Token));
        }

        [TestMethod]
        public async Task ReceiveAsync_ShouldProcessMessageEnvelope()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            var mockEnvelope = new Mock<IMessageEnvelope>();
            var headers = new Dictionary<string, string> { { "SenderId", "sender" } };
            var metadata = new Dictionary<string, object> { { "Timestamp", DateTimeOffset.UtcNow } };

            mockEnvelope.Setup(e => e.Id).Returns("msg-123");
            mockEnvelope.Setup(e => e.Payload).Returns("test message");
            mockEnvelope.Setup(e => e.Headers).Returns(new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(headers));
            mockEnvelope.Setup(e => e.Metadata).Returns(new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(metadata));

            // Act
            var resultEnvelope = await actor.ReceiveAsync(mockEnvelope.Object);

            // Assert
            Assert.AreSame(mockEnvelope.Object, actor.LastReceivedMessage);
            Assert.AreSame(mockEnvelope.Object, resultEnvelope); // TestActor just returns the same envelope
            Assert.AreEqual("msg-123", resultEnvelope.Id);
            Assert.AreEqual("test message", resultEnvelope.Payload);
            Assert.AreEqual("sender", resultEnvelope.Headers["SenderId"]);
            Assert.IsNotNull(resultEnvelope.Metadata["Timestamp"]);
        }

        [TestMethod]
        public async Task ReceiveAsync_ShouldHandleExceptions()
        {
            // Arrange
            var actor = new TestActor("test-actor-1") { ShouldThrowOnReceive = true };
            var mockEnvelope = new Mock<IMessageEnvelope>();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => actor.ReceiveAsync(mockEnvelope.Object));
        }

        [TestMethod]
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
            Assert.IsTrue(actor.ShutdownCalled);
            Assert.AreEqual(ActorState.Stopped, actor.State);
            Assert.IsNotNull(stateChangeArgs);
            Assert.AreEqual(ActorState.Active, stateChangeArgs.PreviousState);
            Assert.AreEqual(ActorState.Stopped, stateChangeArgs.NewState);
            Assert.AreEqual("Shutdown completed", stateChangeArgs.Reason);
        }

        [TestMethod]
        public async Task ShutdownAsync_ShouldRespectCancellationToken()
        {
            // Arrange
            var actor = new TestActor("test-actor-1");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => actor.ShutdownAsync(cts.Token));
        }

        [TestMethod]
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
            Assert.AreEqual(2, stateChanges.Count);
            
            // First state change: Initializing -> Active
            Assert.AreEqual(ActorState.Initializing, stateChanges[0].PreviousState);
            Assert.AreEqual(ActorState.Active, stateChanges[0].NewState);
            
            // Second state change: Active -> Stopped
            Assert.AreEqual(ActorState.Active, stateChanges[1].PreviousState);
            Assert.AreEqual(ActorState.Stopped, stateChanges[1].NewState);
        }

        [DataTestMethod]
        [DataRow(ActorState.Initializing)]
        [DataRow(ActorState.Active)]
        [DataRow(ActorState.Inactive)]
        [DataRow(ActorState.Stopping)]
        [DataRow(ActorState.Stopped)]
        [DataRow(ActorState.Faulted)]
        public void ActorState_ShouldSupportAllDefinedStates(ActorState expectedState)
        {
            // This test verifies that all enum values are properly defined
            // and can be used in actor implementations
            Assert.IsTrue(Enum.IsDefined(typeof(ActorState), expectedState));
        }
    }

    /// <summary>
    /// Unit tests for the ActorStateChangedEventArgs class.
    /// Tests verify proper event argument construction and properties.
    /// </summary>
    [TestClass]
    public class ActorStateChangedEventArgsTests
    {
        [TestMethod]
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
            Assert.AreEqual(previousState, eventArgs.PreviousState);
            Assert.AreEqual(newState, eventArgs.NewState);
            Assert.AreEqual(reason, eventArgs.Reason);
            Assert.IsTrue(eventArgs.Timestamp >= beforeTimestamp);
            Assert.IsTrue(eventArgs.Timestamp <= afterTimestamp);
        }

        [TestMethod]
        public void Constructor_ShouldHandleNullReason()
        {
            // Arrange & Act
            var eventArgs = new ActorStateChangedEventArgs(ActorState.Active, ActorState.Stopped);

            // Assert
            Assert.IsNull(eventArgs.Reason);
            Assert.AreEqual(ActorState.Active, eventArgs.PreviousState);
            Assert.AreEqual(ActorState.Stopped, eventArgs.NewState);
        }

        [TestMethod]
        public void Timestamp_ShouldBeSetToCurrentUtcTime()
        {
            // Arrange
            var beforeCreation = DateTimeOffset.UtcNow;

            // Act
            var eventArgs = new ActorStateChangedEventArgs(ActorState.Active, ActorState.Stopped);
            var afterCreation = DateTimeOffset.UtcNow;

            // Assert
            Assert.IsTrue(eventArgs.Timestamp >= beforeCreation);
            Assert.IsTrue(eventArgs.Timestamp <= afterCreation);
            // DateTimeOffset preserves timezone information, so we check the offset instead
            Assert.AreEqual(TimeSpan.Zero, eventArgs.Timestamp.Offset);
        }
    }
} 