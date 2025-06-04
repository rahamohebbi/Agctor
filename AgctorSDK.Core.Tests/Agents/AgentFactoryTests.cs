using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using Moq;
using Xunit;

namespace AgctorSDK.Core.Tests.Agents;

public class AgentFactoryTests
{
    private readonly Mock<IActorRuntimeAdapter> _mockRuntimeAdapter;
    private readonly AgentFactory _agentFactory;

    public AgentFactoryTests()
    {
        _mockRuntimeAdapter = new Mock<IActorRuntimeAdapter>();
        _agentFactory = new AgentFactory(_mockRuntimeAdapter.Object); 

        // Specific setup for when HumanAgentAdapter is the generic type argument for SpawnActorAsync
        // This is the setup we expect to be hit by the AgentFactory's reflection-based call.
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
        
        // NO general fallback for SpawnActorAsync<IAgent> to ensure the specific one above is tested.
        // If the HumanAgentAdapter spawn doesn't match the setup above, Moq will likely return null or error,
        // which will help diagnose if the specific generic setup is being missed.
    }

    [Theory]
    [InlineData("HumanAgentAdapter")]
    [InlineData("human")]
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
        Assert.NotNull(spawnedAgent); // If null, the SpawnActorAsync<HumanAgentAdapter> mock might not have been hit
        var humanAdapter = Assert.IsType<HumanAgentAdapter>(spawnedAgent);
        Assert.Equal(expectedAgentId, humanAdapter.Id);

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

        Assert.Equal("mock human input from test", humanAdapter.HumanResponse);
        Assert.Equal(AgentStatus.Completed, humanAdapter.Status);
    }

    [Fact]
    public void GetRegisteredAgentTypes_IncludesHumanAgentAdapterAndHumanAlias()
    {
        // Arrange & Act
        var registeredTypes = _agentFactory.GetRegisteredAgentTypes().ToList();

        // Assert
        Assert.Contains("HumanAgentAdapter", registeredTypes);
        Assert.Contains("human", registeredTypes);
    }
} 