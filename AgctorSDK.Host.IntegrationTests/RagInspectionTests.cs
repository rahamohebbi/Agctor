using System.Text.Json;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Rag;
using AgctorSDK.Core.ProjectMemory.Tools;
using AgctorSDK.Core.Rag;
using AgctorSDK.Host.Services.Sessions;
using AgctorSDK.Host.Services.Models;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class RagInspectionTests
{
    private sealed class MemoryStore : IRagInspectionStore
    {
        public List<RagInspectionRecord> Snapshots { get; } = [];
        public Task SaveRagInspectionAsync(RagInspectionRecord record, CancellationToken ct = default)
        {
            lock (Snapshots) Snapshots.Add(JsonSerializer.Deserialize<RagInspectionRecord>(JsonSerializer.Serialize(record))!);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<RagInspectionSummary>> GetRagInspectionsAsync(string sessionId, string? turnGroupId = null, string? traceId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RagInspectionRecord?> GetRagInspectionAsync(string sessionId, string id, CancellationToken ct = default) => throw new NotImplementedException();
    }
    private static (RagContextService Service, Mock<IRagProviderAdapter> Adapter, MemoryStore Store) Build()
    {
        var factory = new Mock<IRagProviderAdapterFactory>();
        factory.Setup(f => f.GetDefaultProviderId()).Returns("Graphiti");
        var adapter = new Mock<IRagProviderAdapter>();
        adapter.Setup(a => a.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RagHealthResult(RagHealthStatus.Healthy, "Ready"));
        adapter.Setup(a => a.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RagQueryResult(
            [new("Raha is Ryan's parent.", .91, "people/raha/profile.md", new Dictionary<string,string> { ["api_key"]="secret", ["entity"]="raha" })],
            "provider-trace", """{"results":[{"text":"Raha is Ryan's parent."}],"authorization":"secret"}"""));
        factory.Setup(f => f.CreateProvider("Graphiti")).Returns(adapter.Object);
        var store = new MemoryStore();
        return (new RagContextService(factory.Object, store), adapter, store);
    }

    [Fact]
    public async Task Retrieval_CapturesStagesResultsPreparedContextAndRedactedResponse()
    {
        var (service, _, store) = Build();
        using var scope = RagInspectionScope.Begin(new("session", "turn", "trace", "query-node", "person-query", "chat override"));
        var context = await service.InspectContextAsync("person-query", "people", "graph_rag", "who is Ryan?", null, async () =>
            (await service.BuildAppendixAsync(new("who is Ryan?", CollectionId:"people", Mode:RagQueryMode.Graph))).Appendix + "\nAgent instructions");
        var record = store.Snapshots.Last();
        record.Outcome.Should().Be("success");
        record.ProviderSource.Should().Be("chat override");
        record.PreparedContext.Should().Be(context);
        record.Results.Single().Text.Should().Contain("Ryan");
        record.Results.Single().Metadata!["api_key"].Should().Be("[redacted]");
        record.RawResponseJson.Should().NotContain("secret").And.Contain("[redacted]");
        record.ProviderTraceId.Should().Be("provider-trace");
        record.CollectionId.Should().Be("people");
        record.Correlation.NodeId.Should().Be("query-node");
        store.Snapshots.Should().Contain(r => r.Stages.Any(s => s.Name == "Searching" && s.Status == "running"));
        record.Stages.Select(s => s.Name).Should().ContainInOrder("Provider selected", "Health check", "Searching", "Results received", "Context prepared");
    }

    [Theory]
    [InlineData("None", "disabled")]
    [InlineData("Graphiti", "unavailable")]
    public async Task MarkdownFallback_RecordsReasonAndTheActualPreparedMarkdown(string provider, string outcome)
    {
        var (service, adapter, store) = Build();
        adapter.Setup(a => a.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RagHealthResult(RagHealthStatus.Unavailable, "Provider offline"));
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","samples","people-project"));
        var loader = new ProjectLoader();
        var ctx = await loader.LoadAsync(root);
        var spec = ctx.AgentSpecs.First(a => a.Id == "person-query");
        using var scope = RagInspectionScope.Begin(new("s", "t"));
        var appendix = await PersonMemoryMarkdownContextBuilder.BuildAppendixAsync(new(loader,new EntityRegistry()), spec, root,
            "person_1", "rag", "who is Ryan?", default, ragService:service, ragOptions:new(provider));
        var record = store.Snapshots.Last();
        record.Outcome.Should().Be(outcome);
        record.MarkdownFallback.Should().BeTrue();
        record.PreparedContext.Should().Be(appendix);
        record.PreparedContext.Should().Contain("on-disk markdown");
        adapter.Verify(a => a.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancellation_IsRecordedAndPropagated()
    {
        var (service, adapter, store) = Build();
        using var cancel = new CancellationTokenSource();
        adapter.Setup(a => a.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>())).Returns(async () =>
        { cancel.Cancel(); await Task.Delay(1, cancel.Token); return new RagQueryResult([]); });
        using var scope = RagInspectionScope.Begin(new("s", "t"));
        await ((Func<Task>)(() => service.InspectContextAsync("q", "scenario", "rag", "q", null, async () =>
            (await service.BuildAppendixAsync(new("q"), cancel.Token)).Appendix))).Should().ThrowAsync<OperationCanceledException>();
        store.Snapshots.Last().Outcome.Should().Be("cancelled");
        store.Snapshots.Last().CompletedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null, "no_results")]
    [InlineData("Search failed with HTTP 503", "failed")]
    public async Task EmptySearchAndProviderFailure_RemainDistinct(string? error, string expected)
    {
        var (service, adapter, store) = Build();
        adapter.Setup(a => a.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResult([], RawDebugJson: "{}", Error:error));
        using var scope = RagInspectionScope.Begin(new("s","t"));
        await service.InspectContextAsync("q","scenario","rag","q",null,async()=>
            (await service.BuildAppendixAsync(new("q"))).Appendix);
        store.Snapshots.Last().Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task MarkdownOnlyStep_IsNotMistakenForDisabledOrFailedRetrieval()
    {
        var (service, adapter, store) = Build();
        using var scope = RagInspectionScope.Begin(new("s","t"));
        await service.InspectContextAsync("q","scenario","markdown_all","q",null,()=>Task.FromResult("markdown context"));
        store.Snapshots.Last().Outcome.Should().Be("not_requested");
        store.Snapshots.Last().MarkdownFallback.Should().BeFalse();
        adapter.Verify(a => a.GetHealthAsync(It.IsAny<CancellationToken>()),Times.Never);
    }

    [Fact]
    public async Task ParallelNodesAndChats_HaveIndependentRecords()
    {
        var (service, _, store) = Build();
        async Task Run(string session, string node)
        {
            using var scope = RagInspectionScope.Begin(new(session,"turn",NodeId:node));
            await service.InspectContextAsync(node,"scenario","rag",node,null,async()=>
            { await Task.Yield();return (await service.BuildAppendixAsync(new(node))).Appendix; });
        }
        await Task.WhenAll(Run("a","one"),Run("a","two"),Run("b","one"));
        var finals = store.Snapshots.Where(r => r.CompletedAt != null).ToList();
        finals.Should().HaveCount(3);
        finals.Select(r=>r.Id).Distinct().Should().HaveCount(3);
        finals.Should().OnlyContain(r=>r.Query == r.Correlation.NodeId);
        RagInspectionScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task Records_SurviveReload_FilterByTurnAndSession_AndAreRemovedWithSession()
    {
        var path = Path.Combine(Path.GetTempPath(),"rag-inspector-"+Guid.NewGuid()+".db");
        try
        {
            var store = new SqliteSessionStore(path);
            await store.CreateSessionAsync("a");await store.CreateSessionAsync("b");
            var record = new RagInspectionRecord { Correlation = new("a","turn-1","trace"), PreparedContext="original context" };
            await store.SaveRagInspectionAsync(record);
            await store.SaveRagInspectionAsync(new(){Correlation=new("a","turn-2")});
            var reloaded = new SqliteSessionStore(path);
            (await reloaded.GetRagInspectionsAsync("a","turn-1")).Should().ContainSingle();
            (await reloaded.GetRagInspectionsAsync("a",traceId:"trace")).Should().ContainSingle();
            (await reloaded.GetRagInspectionAsync("b",record.Id)).Should().BeNull();
            (await reloaded.GetRagInspectionAsync("a",record.Id))!.PreparedContext.Should().Be("original context");
            await store.DeleteSessionAsync("a");
            await store.SaveRagInspectionAsync(record); // A late actor reply must not resurrect deleted session diagnostics.
            (await reloaded.GetRagInspectionsAsync("a")).Should().BeEmpty();
        }
        finally { SqliteConnection.ClearAllPools();File.Delete(path); }
    }

    [Fact]
    public void ActorEnvelope_PreservesInspectionCorrelation()
    {
        var scenario = new ScenarioDefinition {Id="people",Flow=new(){GraphId="g"}};
        var plan = ModelSelectionService.Resolve(scenario,null,"llm") with {RagInspection=new("session","turn","trace")};
        var restored = ModelSelectionService.ReadExecutionPlan(ModelSelectionService.ExecutionJson(scenario.Flow,plan));
        restored!.RagInspection.Should().Be(plan.RagInspection);
    }
}
