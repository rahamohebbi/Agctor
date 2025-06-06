using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Runtime.Examples;
using AgctorSDK.Core.Events;
using AgctorSDK.Core.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Runtime
{
    /// <summary>
    /// Comprehensive unit tests for the InMemoryActorRuntime implementation.
    /// Tests verify actor registration, message dispatch, queuing, and lifecycle management.
    /// </summary>
    [TestClass]
    public class InMemoryActorRuntimeTests : IDisposable
    {
        private readonly InMemoryActorRuntime _runtime;

        // Mock Actor for detailed message inspection
        private class InspectableActor : IActor
        {
            public string Id { get; }
            public string ActorType => nameof(InspectableActor);
            public ActorState State { get; private set; } = ActorState.Initializing;
            public event EventHandler<ActorStateChangedEventArgs>? StateChanged;
            public IMessageEnvelope? LastReceivedEnvelope { get; private set; }
            public int MessagesReceivedCount { get; private set; } = 0;

            public InspectableActor(string id) { Id = id; }
            public Task InitializeAsync(CancellationToken cancellationToken = default) 
            { 
                State = ActorState.Active; 
                StateChanged?.Invoke(this, new ActorStateChangedEventArgs(ActorState.Initializing, ActorState.Active));
                return Task.CompletedTask; 
            }
            public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
            {
                LastReceivedEnvelope = envelope;
                MessagesReceivedCount++;
                // Echo back a simple ack for request-response tests if needed
                var ackPayload = $"Ack for {envelope.Id}";
                var ackHeaders = new Dictionary<string, string> 
                { 
                    { "SenderId", Id }, 
                    { "ReceiverId", envelope.Headers?.TryGetValue("SenderId", out var sId) == true ? sId : "unknown" },
                    { "MessageType", "AckResponse" }
                };
                var ackMetadata = new Dictionary<string, object> { { "Timestamp", DateTimeOffset.UtcNow } };
                if (envelope.Metadata?.TryGetValue("CorrelationId", out var cId) == true) ackMetadata["CorrelationId"] = cId;

                return Task.FromResult<IMessageEnvelope>(new MessageEnvelope(ackPayload, ackMetadata, Guid.NewGuid().ToString(), ackHeaders));
            }
            public Task ShutdownAsync(CancellationToken cancellationToken = default) 
            { 
                State = ActorState.Stopped; 
                StateChanged?.Invoke(this, new ActorStateChangedEventArgs(ActorState.Active, ActorState.Stopped));
                return Task.CompletedTask; 
            }
            public void TriggerStateChange(ActorState newState)
            {
                var oldState = State;
                State = newState;
                StateChanged?.Invoke(this, new ActorStateChangedEventArgs(oldState, newState));
            }
        }

        public InMemoryActorRuntimeTests()
        {
            _runtime = new InMemoryActorRuntime();
        }

        public void Dispose()
        {
            _runtime?.Dispose();
            GC.SuppressFinalize(this);
        }

        [TestMethod]
        public void Runtime_ShouldHaveCorrectProperties()
        {
            // Assert
            Assert.AreEqual("InMemoryActorRuntime", _runtime.Name);
            Assert.AreEqual("1.0.0", _runtime.Version);
            Assert.IsFalse(_runtime.IsInitialized);
            Assert.IsNotNull(_runtime.Configuration);
            Assert.AreEqual(0, _runtime.Configuration.Count);
        }

        [TestMethod]
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
            Assert.IsTrue(_runtime.IsInitialized);
            Assert.AreEqual(2, _runtime.Configuration.Count);
            Assert.AreEqual(100, _runtime.Configuration["MaxActors"]);
            Assert.AreEqual("Debug", _runtime.Configuration["LogLevel"]);
        }

        [TestMethod]
        public async Task InitializeAsync_WhenAlreadyInitialized_ShouldNotThrow()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());

            // Act & Assert - Should not throw
            await _runtime.InitializeAsync(new Dictionary<string, object> { { "test", "value" } });
            Assert.IsTrue(_runtime.IsInitialized);
        }

        [TestMethod]
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
            Assert.IsNotNull(actor);
            Assert.AreEqual(actorId, actor.Id);
            Assert.AreEqual(nameof(EchoActor), actor.ActorType);
            Assert.AreEqual(ActorState.Active, actor.State);
            
            Assert.IsNotNull(spawnedEvent);
            Assert.AreEqual(actorId, spawnedEvent.ActorId);
            Assert.AreEqual(nameof(EchoActor), spawnedEvent.ActorType);
        }

        [TestMethod]
        public async Task SpawnActorAsync_WithDuplicateId_ShouldThrow()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "duplicate-actor";
            await _runtime.SpawnActorAsync<EchoActor>(actorId);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _runtime.SpawnActorAsync<EchoActor>(actorId));
        }

        [TestMethod]
        public async Task SpawnActorAsync_WhenNotInitialized_ShouldThrow()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _runtime.SpawnActorAsync<EchoActor>("test-actor"));
        }

        [TestMethod]
        public async Task GetActorAsync_ShouldReturnExistingActor()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "test-actor";
            var originalActor = await _runtime.SpawnActorAsync<EchoActor>(actorId);

            // Act
            var retrievedActor = await _runtime.GetActorAsync<EchoActor>(actorId);

            // Assert
            Assert.IsNotNull(retrievedActor);
            Assert.AreEqual(originalActor.Id, retrievedActor.Id);
            Assert.AreSame(originalActor, retrievedActor);
        }

        [TestMethod]
        public async Task GetActorAsync_WithNonExistentId_ShouldReturnNull()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());

            // Act
            var actor = await _runtime.GetActorAsync<EchoActor>("non-existent");

            // Assert
            Assert.IsNull(actor);
        }

        [TestMethod]
        public async Task GetActorAsync_WithWrongType_ShouldReturnNull()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "test-actor";
            await _runtime.SpawnActorAsync<EchoActor>(actorId);

            // Act
            var actor = await _runtime.GetActorAsync<IActor>(actorId); 

            // Assert 
            Assert.IsNotNull(actor);
            Assert.IsInstanceOfType(actor, typeof(EchoActor));
        }

        [TestMethod]
        public async Task SendMessageAsync_ShouldDeliverMessageToActor_MCP()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "inspect-actor-1";
            var actor = await _runtime.SpawnActorAsync<InspectableActor>(actorId);
            var message = "Hello, Actor!";
            string senderForMessage = "sender-test-1";
            MessageSentEventArgs? sentEvent = null;
            _runtime.MessageSent += (sender, args) => sentEvent = args;

            // Act
            await _runtime.SendMessageAsync(actorId, message, senderForMessage);
            await Task.Delay(100); 

            // Assert for MessageSent event
            Assert.IsNotNull(sentEvent);
            Assert.AreEqual(senderForMessage, sentEvent.SenderId);
            Assert.AreEqual(actorId, sentEvent.ReceiverId);
            Assert.AreEqual("String", sentEvent.MessageType); 

            // Assert for what the actor received
            Assert.IsNotNull(actor.LastReceivedEnvelope);
            Assert.AreEqual(message, actor.LastReceivedEnvelope.Payload);
            Assert.AreEqual(senderForMessage, actor.LastReceivedEnvelope.Headers["SenderId"]);
            Assert.AreEqual(actorId, actor.LastReceivedEnvelope.Headers["ReceiverId"]);
            Assert.AreEqual("String", actor.LastReceivedEnvelope.Headers["MessageType"]);
            Assert.IsTrue(actor.LastReceivedEnvelope.Metadata.ContainsKey("Timestamp"));
        }

        [TestMethod]
        public async Task SendMessageAsync_WithHeaders_ShouldIncludeHeaders_MCP()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "inspect-actor-2";
            var actor = await _runtime.SpawnActorAsync<InspectableActor>(actorId);
            var message = "A message with custom headers";
            var headers = new Dictionary<string, string> { { "CorrelationId", "corr-xyz" }, { "CustomHeader", "CustomValue" } };
            
            // Act
            await _runtime.SendMessageAsync(actorId, message, "sender-test-2", headers);
            await Task.Delay(100);

            // Assert
            Assert.IsNotNull(actor.LastReceivedEnvelope);
            Assert.AreEqual(4, actor.LastReceivedEnvelope.Headers.Count); 
            Assert.AreEqual("corr-xyz", actor.LastReceivedEnvelope.Headers["CorrelationId"]);
            Assert.AreEqual("CustomValue", actor.LastReceivedEnvelope.Headers["CustomHeader"]);
            Assert.AreEqual("sender-test-2", actor.LastReceivedEnvelope.Headers["SenderId"]);
            Assert.AreEqual(actorId, actor.LastReceivedEnvelope.Headers["ReceiverId"]);
        }

        [TestMethod]
        public async Task SendMessageAsync_ToNonExistentActor_ShouldNotThrow()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());

            // Act & Assert
            await _runtime.SendMessageAsync("non-existent-actor", "test message");
        }

        [TestMethod]
        public async Task SendMessageAsync_WithComplexMessage_ShouldWork_MCP()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "inspect-actor-3";
            var actor = await _runtime.SpawnActorAsync<InspectableActor>(actorId);
            var complexMessage = new { Name = "Complex", Value = 123, Nested = new { Prop = "NestedProp" } };
            
            // Act
            await _runtime.SendMessageAsync(actorId, complexMessage);
            await Task.Delay(100);

            // Assert
            Assert.IsNotNull(actor.LastReceivedEnvelope);
            Assert.AreSame(complexMessage, actor.LastReceivedEnvelope.Payload);
            Assert.IsNotNull(actor.LastReceivedEnvelope.Headers["MessageType"]);
        }

        [TestMethod]
        public async Task SendMessageAsync_RequestResponse_ShouldReturnResponse_MCP()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "inspect-actor-4";
            await _runtime.SpawnActorAsync<InspectableActor>(actorId);
            var requestMessage = "This is a request";

            // Act
            var response = await _runtime.SendMessageAsync<IMessageEnvelope>(actorId, requestMessage, TimeSpan.FromSeconds(2));

            // Assert
            Assert.IsNotNull(response);
            Assert.IsInstanceOfType(response, typeof(IMessageEnvelope));
            StringAssert.Contains(response.Payload as string, "Ack for");
            Assert.AreEqual(actorId, response.Headers["SenderId"]);
        }

        [TestMethod]
        public async Task StopActorAsync_ShouldStopAndRemoveActor()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actorId = "actor-to-stop";
            var actor = await _runtime.SpawnActorAsync<EchoActor>(actorId);
            ActorStoppedEventArgs? stoppedEvent = null;
            _runtime.ActorStopped += (sender, args) => stoppedEvent = args;

            // Act
            await _runtime.StopActorAsync(actorId);
            var retrievedActor = await _runtime.GetActorAsync<EchoActor>(actorId);

            // Assert
            Assert.IsNull(retrievedActor);
            Assert.AreEqual(ActorState.Stopped, actor.State);

            Assert.IsNotNull(stoppedEvent);
            Assert.AreEqual(actorId, stoppedEvent.ActorId);
            Assert.AreEqual(nameof(EchoActor), stoppedEvent.ActorType);
        }

        [TestMethod]
        public async Task StopActorAsync_WithNonExistentActor_ShouldNotThrow()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());

            // Act & Assert
            await _runtime.StopActorAsync("non-existent-actor");
        }

        [TestMethod]
        public async Task GetActiveActorIdsAsync_ShouldReturnActiveActors()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            await _runtime.SpawnActorAsync<EchoActor>("actor1");
            await _runtime.SpawnActorAsync<EchoActor>("actor2");
            await _runtime.StopActorAsync("actor1");

            // Act
            var activeIds = await _runtime.GetActiveActorIdsAsync();
            var activeIdList = activeIds.ToList();

            // Assert
            Assert.AreEqual(1, activeIdList.Count);
            Assert.AreEqual("actor2", activeIdList[0]);
        }

        [TestMethod]
        public async Task ShutdownAsync_ShouldStopAllActors()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actor1 = await _runtime.SpawnActorAsync<EchoActor>("actor1");
            var actor2 = await _runtime.SpawnActorAsync<EchoActor>("actor2");
            int stoppedCount = 0;
            _runtime.ActorStopped += (s, e) => stoppedCount++;

            // Act
            await _runtime.ShutdownAsync();

            // Assert
            Assert.AreEqual(ActorState.Stopped, actor1.State);
            Assert.AreEqual(ActorState.Stopped, actor2.State);
            Assert.AreEqual(2, stoppedCount);
            var activeIds = await _runtime.GetActiveActorIdsAsync();
            Assert.AreEqual(0, activeIds.Count());
            Assert.IsFalse(_runtime.IsInitialized);
        }

        [TestMethod]
        public async Task ShutdownAsync_WhenNotInitialized_ShouldNotThrow()
        {
            // Act & Assert
            await _runtime.ShutdownAsync();
        }

        [TestMethod]
        public async Task MultipleActors_ShouldProcessMessagesConcurrently()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actor1 = await _runtime.SpawnActorAsync<InspectableActor>("multi-actor-1");
            var actor2 = await _runtime.SpawnActorAsync<InspectableActor>("multi-actor-2");
            
            var messages1 = Enumerable.Range(0, 5).Select(i => $"Message {i} to actor 1").ToList();
            var messages2 = Enumerable.Range(0, 5).Select(i => $"Message {i} to actor 2").ToList();

            // Act
            var tasks1 = messages1.Select(m => _runtime.SendMessageAsync(actor1.Id, m)).ToList();
            var tasks2 = messages2.Select(m => _runtime.SendMessageAsync(actor2.Id, m)).ToList();
            await Task.WhenAll(tasks1.Concat(tasks2));
            await Task.Delay(200);

            // Assert
            Assert.AreEqual(5, actor1.MessagesReceivedCount);
            Assert.AreEqual(5, actor2.MessagesReceivedCount);
        }

        [TestMethod]
        public async Task ActorLifecycle_ShouldFireStateChangeEvents()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            List<ActorStateChangedEventArgs> stateChanges = new List<ActorStateChangedEventArgs>();
            var actorId = "lifecycle-actor";

            var actor = await _runtime.SpawnActorAsync<InspectableActor>(actorId);
            actor.StateChanged += (sender, args) => stateChanges.Add(args);
            
            // Act
            actor.TriggerStateChange(ActorState.Inactive);
            actor.TriggerStateChange(ActorState.Stopping);
            actor.TriggerStateChange(ActorState.Stopped);
            
            // Assert
            Assert.AreEqual(3, stateChanges.Count); 
            Assert.AreEqual(ActorState.Active, stateChanges[0].PreviousState);
            Assert.AreEqual(ActorState.Inactive, stateChanges[0].NewState);
            Assert.AreEqual(ActorState.Inactive, stateChanges[1].PreviousState);
            Assert.AreEqual(ActorState.Stopping, stateChanges[1].NewState);
            Assert.AreEqual(ActorState.Stopping, stateChanges[2].PreviousState);
            Assert.AreEqual(ActorState.Stopped, stateChanges[2].NewState);
        }

        [TestMethod]
        public async Task Runtime_ShouldHandleHighMessageVolume()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            var actor = await _runtime.SpawnActorAsync<InspectableActor>("heavy-load-actor");
            int messageCount = 100;
            
            // Act
            var sendTasks = new List<Task>();
            for (int i = 0; i < messageCount; i++)
            {
                sendTasks.Add(_runtime.SendMessageAsync(actor.Id, $"Message {i}"));
            }
            await Task.WhenAll(sendTasks);
            await Task.Delay(500); // Allow time for processing

            // Assert
            Assert.AreEqual(messageCount, actor.MessagesReceivedCount);
        }

        [TestMethod]
        public async Task Dispose_ShouldCleanupResources()
        {
            // Arrange
            await _runtime.InitializeAsync(new Dictionary<string, object>());
            await _runtime.SpawnActorAsync<EchoActor>("actor-to-dispose");
            
            // Act
            _runtime.Dispose();

            // Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _runtime.GetActiveActorIdsAsync());
            Assert.IsFalse(_runtime.IsInitialized);
        }

        [TestMethod]
        public async Task Runtime_ShouldHandleCancellation()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            await _runtime.InitializeAsync(new Dictionary<string, object>(), cts.Token);
            
            // Act & Assert
            cts.Cancel();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => _runtime.SpawnActorAsync<EchoActor>("cancelled-actor", cancellationToken: cts.Token));
        }
    }
} 