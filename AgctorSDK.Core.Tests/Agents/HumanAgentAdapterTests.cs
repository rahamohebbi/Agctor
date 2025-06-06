using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AgctorSDK.Core.Tests.Agents
{
    [TestClass]
    public class HumanAgentAdapterTests
    {
        private Mock<IAgentFactory> _mockAgentFactory;
        private Mock<IActorRuntimeAdapter> _mockRuntimeAdapter;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockRuntimeAdapter = new Mock<IActorRuntimeAdapter>();
            _mockAgentFactory = new Mock<IAgentFactory>();
            _mockAgentFactory.Setup(f => f.RuntimeAdapter).Returns(_mockRuntimeAdapter.Object);
        }

        private HumanAgentAdapter CreateAdapter(string id = "test-human-agent")
        {
            var adapter = new HumanAgentAdapter(id);
            adapter.SetAgentFactory(_mockAgentFactory.Object); 
            return adapter;
        }

        [TestMethod]
        public async Task ProcessPromptAsync_CallsRequestHumanInput_AndSetsStatusCorrectly()
        {
            // Arrange
            var adapter = CreateAdapter();
            var prompt = "Please provide input.";
            var expectedResponse = "Human says hi!";
            var statusChanges = new List<AgentStatus>();

            adapter.StatusChanged += (s, e) => statusChanges.Add(e.NewStatus);

            _mockRuntimeAdapter
                .Setup(r => r.RequestHumanInputAsync(adapter.Id, prompt, "Please enter your response below. Type '::done' on a new line to finish.", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResponse);

            // Act
            await adapter.ProcessPromptAsync(prompt, CancellationToken.None);

            // Assert
            _mockRuntimeAdapter.Verify(r => r.RequestHumanInputAsync(adapter.Id, prompt, "Please enter your response below. Type '::done' on a new line to finish.", It.IsAny<CancellationToken>()), Times.Once);
            Assert.AreEqual(expectedResponse, adapter.HumanResponse);
            Assert.AreEqual(AgentStatus.Completed, adapter.Status); 

            CollectionAssert.Contains(statusChanges, AgentStatus.Working); 
            CollectionAssert.Contains(statusChanges, AgentStatus.WaitingForHumanInput);
            CollectionAssert.Contains(statusChanges, AgentStatus.Completed);
            Assert.IsTrue(statusChanges.IndexOf(AgentStatus.Working) < statusChanges.IndexOf(AgentStatus.WaitingForHumanInput));
            Assert.IsTrue(statusChanges.IndexOf(AgentStatus.WaitingForHumanInput) < statusChanges.IndexOf(AgentStatus.Completed));
        }

        [TestMethod]
        public async Task ProcessPromptAsync_SetsHumanResponse_OnSuccessfulInput()
        {
            // Arrange
            var adapter = CreateAdapter();
            var prompt = "Input needed.";
            var humanInput = "This is the input.";
            _mockRuntimeAdapter
                .Setup(r => r.RequestHumanInputAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(humanInput);

            // Act
            await adapter.ProcessPromptAsync(prompt, CancellationToken.None);

            // Assert
            Assert.AreEqual(humanInput, adapter.HumanResponse);
        }

        [TestMethod]
        public async Task ProcessPromptAsync_SetsStatusToFailed_OnRuntimeAdapterException()
        {
            // Arrange
            var adapter = CreateAdapter();
            var prompt = "Input?";
            var statusChanges = new List<AgentStatus>();
            adapter.StatusChanged += (s, e) => statusChanges.Add(e.NewStatus);

            _mockRuntimeAdapter
                .Setup(r => r.RequestHumanInputAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Runtime failed"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => adapter.ProcessPromptAsync(prompt, CancellationToken.None));
            Assert.IsNull(adapter.HumanResponse);
            Assert.AreEqual(AgentStatus.Failed, adapter.Status);
            CollectionAssert.Contains(statusChanges, AgentStatus.WaitingForHumanInput);
            CollectionAssert.Contains(statusChanges, AgentStatus.Failed);
        }

        [TestMethod]
        public async Task ProcessPromptAsync_HandlesOperationCanceledException_AndSetsStatusToFailed()
        {
            // Arrange
            var adapter = CreateAdapter();
            var prompt = "Input please.";
             var statusChanges = new List<AgentStatus>();
            adapter.StatusChanged += (s, e) => statusChanges.Add(e.NewStatus);

            _mockRuntimeAdapter
                .Setup(r => r.RequestHumanInputAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => adapter.ProcessPromptAsync(prompt, CancellationToken.None));
            Assert.IsNull(adapter.HumanResponse);
            Assert.AreEqual(AgentStatus.Failed, adapter.Status);
            CollectionAssert.Contains(statusChanges, AgentStatus.WaitingForHumanInput);
            CollectionAssert.Contains(statusChanges, AgentStatus.Failed);
        }

        [TestMethod]
        public async Task ProcessPromptAsync_ThrowsAndSetsFailed_IfAgentFactoryNotSet()
        {
            // Arrange
            var adapter = new HumanAgentAdapter("test-agent-no-factory"); 
            var prompt = "Test prompt";
            var statusChanges = new List<AgentStatus>();
            adapter.StatusChanged += (s, e) => statusChanges.Add(e.NewStatus);

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => adapter.ProcessPromptAsync(prompt, CancellationToken.None));
            StringAssert.Contains(ex.Message, "AgentFactory not initialized");
            Assert.IsNull(adapter.HumanResponse);
            Assert.AreEqual(AgentStatus.Failed, adapter.Status);
            CollectionAssert.Contains(statusChanges, AgentStatus.Working);
            CollectionAssert.Contains(statusChanges, AgentStatus.Failed); 
            CollectionAssert.DoesNotContain(statusChanges, AgentStatus.WaitingForHumanInput);
        }

        [TestMethod]
        public async Task AssignSubtaskAsync_ThrowsNotSupportedException()
        {
            // Arrange
            var adapter = CreateAdapter();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<NotSupportedException>(() => adapter.AssignSubtaskAsync("subtask prompt"));
        }
    }
} 