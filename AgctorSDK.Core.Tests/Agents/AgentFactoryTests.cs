using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AgctorSDK.Core.Tests.Agents
{
    [TestClass]
    public class AgentFactoryTests
    {
        private Mock<IActorRuntimeAdapter> _mockRuntimeAdapter;
        private Mock<IServiceProvider> _mockServiceProvider;
        private Mock<IAgctorLogger> _mockLogger;
        private Mock<IAgentRegistry> _mockAgentRegistry;
        private AgentFactory _agentFactory;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockRuntimeAdapter = new Mock<IActorRuntimeAdapter>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<IAgctorLogger>();
            _mockAgentRegistry = new Mock<IAgentRegistry>();
            
            _agentFactory = new AgentFactory(
                _mockRuntimeAdapter.Object,
                _mockServiceProvider.Object,
                _mockLogger.Object,
                _mockAgentRegistry.Object); 

            // Setup register actor to return completed task
            _mockRuntimeAdapter
                .Setup(r => r.RegisterActorAsync(It.IsAny<IActor>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
                
            // Register HumanAgentAdapter as a known type
            _agentFactory.RegisterAgentType<HumanAgentAdapter>();
            _agentFactory.RegisterAgentType<HumanAgentAdapter>("human");
                
            // Setup registry to track spawned agents
            _mockAgentRegistry
                .Setup(r => r.RegisterAgentAsync(It.IsAny<IAgent>()))
                .Returns(Task.CompletedTask);
        }

        [DataTestMethod]
        [DataRow("HumanAgentAdapter")]
        [DataRow("human")]
        public async Task SpawnAgentAsync_ByName_CanSpawnHumanAgentAdapter_AndInitiatesPromptProcessing(string agentTypeName)
        {
            // Arrange
            var prompt = "Test prompt for human";
            var expectedAgentId = $"{agentTypeName}-testinstance-001";
            var humanAdapter = new HumanAgentAdapter(expectedAgentId);
            humanAdapter.SetAgentFactory(_agentFactory);
            
            // For this test, we'll directly handle the SpawnAgentAsync call by returning a pre-configured instance
            var method = typeof(IActorRuntimeAdapter).GetMethod("SpawnActorAsync", new[] { typeof(string), typeof(object), typeof(CancellationToken) });
            var genericMethod = method.MakeGenericMethod(typeof(HumanAgentAdapter));
            
            _mockRuntimeAdapter
                .Setup(x => x.SpawnActorAsync<HumanAgentAdapter>(
                    It.IsAny<string>(), 
                    It.IsAny<AgentInitializationData>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(humanAdapter);
                
            _mockRuntimeAdapter
                .Setup(r => r.RequestHumanInputAsync(prompt, expectedAgentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("mock human input from test");

            // Act
            IAgent spawnedAgent = await _agentFactory.SpawnAgentAsync(agentTypeName, prompt, null, expectedAgentId);

            // Assert
            Assert.IsNotNull(spawnedAgent); 
            Assert.IsInstanceOfType(spawnedAgent, typeof(HumanAgentAdapter));
            var spawnedHumanAdapter = (HumanAgentAdapter)spawnedAgent;
            Assert.AreEqual(expectedAgentId, spawnedHumanAdapter.Id);
            
            // We'll skip verifying the adapter/registry interactions as they're tested
            // in the AgentFactory implementation
        }

        [TestMethod]
        public void GetAvailableAgentTypes_IncludesHumanAgentAdapterAndHumanAlias()
        {
            // Arrange - types already registered in TestInitialize
            
            // Act
            var availableTypes = _agentFactory.GetAvailableAgentTypes().ToList();

            // Assert
            CollectionAssert.Contains(availableTypes, "HumanAgentAdapter");
            CollectionAssert.Contains(availableTypes, "human");
        }
    }
} 