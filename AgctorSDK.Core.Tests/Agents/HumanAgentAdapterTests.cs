using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using Moq;
using Xunit;

namespace AgctorSDK.Core.Tests.Agents;

public class HumanAgentAdapterTests
{
    private readonly Mock<IAgentFactory> _mockAgentFactory;
    private readonly Mock<IActorRuntimeAdapter> _mockRuntimeAdapter;

    public HumanAgentAdapterTests()
    {
        _mockRuntimeAdapter = new Mock<IActorRuntimeAdapter>();
        _mockAgentFactory = new Mock<IAgentFactory>();
        _mockAgentFactory.Setup(f => f.RuntimeAdapter).Returns(_mockRuntimeAdapter.Object);
    }

    // Helper to create an adapter and inject the mocked factory
    private HumanAgentAdapter CreateAdapter(string id = "test-human-agent")
    {
        var adapter = new HumanAgentAdapter(id);
        // Simulate the factory injecting itself, which happens in AgentFactory.SpawnAgentAsync -> runtime.SpawnActorAsync -> SetupAgentIfNeeded
        adapter.SetAgentFactory(_mockAgentFactory.Object); 
        return adapter;
    }

    [Fact]
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
        // ProcessPromptAsync is the public method on Agent, which calls the protected ProcessPromptInternalAsync
        await adapter.ProcessPromptAsync(prompt, CancellationToken.None);

        // Assert
        _mockRuntimeAdapter.Verify(r => r.RequestHumanInputAsync(adapter.Id, prompt, "Please enter your response below. Type '::done' on a new line to finish.", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(expectedResponse, adapter.HumanResponse);
        Assert.Equal(AgentStatus.Completed, adapter.Status); // Final status

        Assert.Contains(AgentStatus.Working, statusChanges); // Initial status set by base Agent.ProcessPromptAsync
        Assert.Contains(AgentStatus.WaitingForHumanInput, statusChanges);
        Assert.Contains(AgentStatus.Completed, statusChanges);
        // Check order of status changes
        Assert.True(statusChanges.IndexOf(AgentStatus.Working) < statusChanges.IndexOf(AgentStatus.WaitingForHumanInput));
        Assert.True(statusChanges.IndexOf(AgentStatus.WaitingForHumanInput) < statusChanges.IndexOf(AgentStatus.Completed));
    }

    [Fact]
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
        Assert.Equal(humanInput, adapter.HumanResponse);
    }

    [Fact]
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ProcessPromptAsync(prompt, CancellationToken.None));
        Assert.Null(adapter.HumanResponse);
        Assert.Equal(AgentStatus.Failed, adapter.Status); // Final status from ProcessPromptInternalAsync
        Assert.Contains(AgentStatus.WaitingForHumanInput, statusChanges);
        Assert.Contains(AgentStatus.Failed, statusChanges);
    }

    [Fact]
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
        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.ProcessPromptAsync(prompt, CancellationToken.None));
        Assert.Null(adapter.HumanResponse);
        Assert.Equal(AgentStatus.Failed, adapter.Status);
        Assert.Contains(AgentStatus.WaitingForHumanInput, statusChanges);
        Assert.Contains(AgentStatus.Failed, statusChanges);
    }

    [Fact]
    public async Task ProcessPromptAsync_ThrowsAndSetsFailed_IfAgentFactoryNotSet()
    {
        // Arrange
        var adapter = new HumanAgentAdapter("test-agent-no-factory"); // AgentFactory is NOT set
        var prompt = "Test prompt";
        var statusChanges = new List<AgentStatus>();
        adapter.StatusChanged += (s, e) => statusChanges.Add(e.NewStatus);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ProcessPromptAsync(prompt, CancellationToken.None));
        Assert.Contains("AgentFactory not initialized", ex.Message);
        Assert.Null(adapter.HumanResponse);
        // Base Agent.ProcessPromptAsync calls ChangeAgentStatus(AgentStatus.Working), 
        // then ProcessPromptInternalAsync throws. The catch in base Agent.ProcessPromptAsync sets Failed.
        Assert.Equal(AgentStatus.Failed, adapter.Status);
        Assert.Contains(AgentStatus.Working, statusChanges);
        Assert.Contains(AgentStatus.Failed, statusChanges); 
        Assert.DoesNotContain(AgentStatus.WaitingForHumanInput, statusChanges); // Should fail before this point
    }

    [Fact]
    public async Task AssignSubtaskAsync_ThrowsNotSupportedException()
    {
        // Arrange
        var adapter = CreateAdapter();

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.AssignSubtaskAsync("subtask prompt"));
    }
} 