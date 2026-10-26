using System.Text.Json;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Rag;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Host.Services.Models;
using AgctorSDK.Host.Services.ProjectMemory;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class RagProviderSelectionTests
{
    private static ModelSelectionPlan Plan(string provider) => ModelSelectionService.Resolve(
        new ScenarioDefinition { Id = "test", RagProviderId = provider }, null, "app");

    [Fact]
    public async Task ConcurrentChats_QueryTheirOwnProvider_AndExplicitNodesWin()
    {
        var factory = new Mock<IRagProviderAdapterFactory>();
        factory.Setup(f => f.GetDefaultProviderId()).Returns(RagProviderIds.LightRag);
        foreach (var id in new[] { RagProviderIds.LightRag, RagProviderIds.Graphiti, RagProviderIds.Cognee })
        {
            var adapter = new Mock<IRagProviderAdapter>();
            adapter.Setup(a => a.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RagHealthResult(RagHealthStatus.Healthy, "ok"));
            adapter.Setup(a => a.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RagQueryResult([new RagContextChunk("context from " + id)]));
            factory.Setup(f => f.CreateProvider(id)).Returns(adapter.Object);
        }
        var service = new RagContextService(factory.Object);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<RagContextAppendixResult> Query(string provider, string? node = null)
        {
            using var scope = ModelSelectionScope.Begin(Plan(provider));
            await ready.Task;
            return await service.BuildAppendixAsync(new("question", node));
        }
        var a = Query(RagProviderIds.Graphiti);
        var b = Query(RagProviderIds.Cognee);
        var node = Query(RagProviderIds.Graphiti, RagProviderIds.LightRag);
        var disabled = Query(RagProviderIds.None);
        ready.SetResult();
        (await a).Appendix.Should().Contain("context from Graphiti");
        (await b).Appendix.Should().Contain("context from Cognee");
        (await node).ProviderId.Should().Be(RagProviderIds.LightRag);
        (await disabled).UsedExternalRag.Should().BeFalse();
        factory.Verify(f => f.CreateProvider(RagProviderIds.None), Times.Never);
        ModelSelectionScope.Plan.Should().BeNull();
        (await service.BuildAppendixAsync(new("question"))).ProviderId.Should().Be(RagProviderIds.LightRag);
        factory.Verify(f => f.CreateProvider(RagProviderIds.Graphiti), Times.Once);
        factory.Verify(f => f.CreateProvider(RagProviderIds.Cognee), Times.Once);
    }

    [Fact]
    public async Task UnavailableSelectedProvider_FallsBackWithoutQueryingGlobalProvider()
    {
        var factory = new Mock<IRagProviderAdapterFactory>();
        factory.Setup(f => f.GetDefaultProviderId()).Returns(RagProviderIds.LightRag);
        var adapter = new Mock<IRagProviderAdapter>();
        adapter.Setup(a => a.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RagHealthResult(RagHealthStatus.Unavailable, "offline"));
        factory.Setup(f => f.CreateProvider(RagProviderIds.Graphiti)).Returns(adapter.Object);
        using var scope = ModelSelectionScope.Begin(Plan(RagProviderIds.Graphiti));
        var result = await new RagContextService(factory.Object).BuildAppendixAsync(new("question"));
        result.FellBack.Should().BeTrue();
        result.ProviderId.Should().Be(RagProviderIds.Graphiti);
        factory.Verify(f => f.CreateProvider(RagProviderIds.LightRag), Times.Never);
    }

    [Theory]
    [InlineData(null, "Graphiti")]
    [InlineData("Cognee", "Cognee")]
    [InlineData("None", "None")]
    public async Task ToolMailbox_ReceivesResolvedProviderExplicitly(string? nodeProvider, string expected)
    {
        var agents = new Mock<IAgentFactory>();
        agents.Setup(a => a.InvokeToolRequestAsync(It.IsAny<string>(), It.Is<ToolRequest>(r =>
                (string)r.Parameters["ragProviderId"] == expected), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { IsSuccess = true, Output = "retrieved" });
        using var scope = ModelSelectionScope.Begin(Plan(RagProviderIds.Graphiti));
        var result = await PlaygroundPersonQueryContextBuilder.BuildFlowAppendixAsync(
            Mock.Of<IProjectLoader>(), Mock.Of<IEntityRegistry>(), agents.Object, new AgentDefinitionSpec { Id = "person-query" },
            "person-query", "/unused", "test", JsonSerializer.SerializeToElement(new { contextStrategy = "rag", ragProviderId = nodeProvider }),
            "question", default);
        result.Should().Be("retrieved");
        agents.VerifyAll();
    }
}
