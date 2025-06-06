using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AgctorSDK.Core.Tests.Agents
{
    [TestClass]
    public class AgentFactoryTests
    {
        private Mock<IActorRuntimeAdapter> _mockRuntimeAdapter;
        private AgentFactory _agentFactory;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockRuntimeAdapter = new Mock<IActorRuntimeAdapter>();
            _agentFactory = new AgentFactory(_mockRuntimeAdapter.Object); 

            _mockRuntimeAdapter
                .Setup(r => r.SpawnActorAsync<HumanAgentAdapter>(
                    It.IsAny<string>(), 
                    It.IsAny<AgentInitializationData>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, AgentInitializationData? initData, CancellationToken ct) =>
                {
                    var humanAdapter = new HumanAgentAdapter(id);
                    if (initData?.AgentFactory != null) 
                    {
                        humanAdapter.SetAgentFactory(initData.AgentFactory); 
                    }
                    return humanAdapter;
                });
        }

        [DataTestMethod]
        [DataRow("HumanAgentAdapter")]
        [DataRow("human")]
        public async Task SpawnAgentAsync_ByName_CanSpawnHumanAgentAdapter_AndInitiatesPromptProcessing(string agentTypeName)
        {
            // Arrange
            var prompt = "Test prompt for human";
            var expectedAgentId = $"{agentTypeName}-testinstance-001"; 

            _mockRuntimeAdapter
                .Setup(r => r.RequestHumanInputAsync(expectedAgentId, prompt, "Please enter your response below. Type '::done' on a new line to finish.", It.IsAny<CancellationToken>()))
                .ReturnsAsync("mock human input from test");

            // Act
            IAgent spawnedAgent = await _agentFactory.SpawnAgentAsync(agentTypeName, prompt, null, expectedAgentId);

            // Assert
            Assert.IsNotNull(spawnedAgent); 
            Assert.IsInstanceOfType(spawnedAgent, typeof(HumanAgentAdapter));
            var humanAdapter = (HumanAgentAdapter)spawnedAgent;
            Assert.AreEqual(expectedAgentId, humanAdapter.Id);

            _mockRuntimeAdapter.Verify(r =>
                r.SpawnActorAsync<HumanAgentAdapter>(
                    expectedAgentId, 
                    It.Is<AgentInitializationData>(d => 
                        d.Prompt == prompt && 
                        d.ParentAgentId == null && 
                        d.AgentFactory == _agentFactory
                    ), 
                    It.IsAny<CancellationToken>()),
                Times.Once 
            );

            _mockRuntimeAdapter.Verify(r =>
                r.RequestHumanInputAsync(
                    expectedAgentId, 
                    prompt,          
                    "Please enter your response below. Type '::done' on a new line to finish.",
                    It.IsAny<CancellationToken>()),
                Times.Once 
            );

            Assert.AreEqual("mock human input from test", humanAdapter.HumanResponse);
            Assert.AreEqual(AgentStatus.Completed, humanAdapter.Status);
        }

        [TestMethod]
        public void GetRegisteredAgentTypes_IncludesHumanAgentAdapterAndHumanAlias()
        {
            // Arrange & Act
            var registeredTypes = _agentFactory.GetRegisteredAgentTypes().ToList();

            // Assert
            CollectionAssert.Contains(registeredTypes, "HumanAgentAdapter");
            CollectionAssert.Contains(registeredTypes, "human");
        }
    }
} 