using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Retrieval;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using AgctorSDK.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Core.Tests;

public sealed class SceneMemoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "agctor-scene-tests-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(_directory, "scene.db");
    private SqliteSceneMemoryStore Store() => new(Database);

    [Fact]
    public async Task Provider_registration_preserves_custom_store_and_supports_shared_retrieval_contract()
    {
        var store = Store();
        var workspace = await store.CreateAsync("Provider");
        workspace = await store.AddEntityAsync(workspace.Id, "CupA", "Object");
        await store.RecordAsync(workspace.Id, workspace.Entities.Single().Id, "location", "Table", null);
        var services = new ServiceCollection();
        services.AddSingleton<ISceneMemoryStore>(store);
        services.AddAgctorSceneMemory(Path.Combine(_directory, "unused.db"));
        services.AddAgctorSceneMemory(Path.Combine(_directory, "also-unused.db"));
        services.AddAgctorRagProviders();
        using var sp = services.BuildServiceProvider();
        Assert.Same(store, sp.GetRequiredService<ISceneMemoryStore>());
        Assert.Single(sp.GetServices<ISceneMemoryProvider>());
        IRetrievalProvider<SceneMemoryQueryRequest, SceneQueryResult> scene = sp.GetRequiredService<ISceneMemoryProvider>();
        IRetrievalProvider<RagQueryRequest, RagQueryResult> rag = sp.GetRequiredService<IRagProviderAdapterFactory>().CreateProvider(RagProviderIds.None);
        Assert.Equal(RagHealthStatus.Healthy, (await scene.GetHealthAsync()).Status);
        Assert.Equal(RagHealthStatus.Healthy, (await rag.GetHealthAsync()).Status);
        var result = await scene.QueryAsync(new SceneMemoryQueryRequest(workspace.Id, "Where is CupA?"));
        Assert.Equal("Table", Assert.Single(result.Facts).Value);
        Assert.Empty((await rag.QueryAsync(new RagQueryRequest("context", null))).Chunks);
        Assert.False(File.Exists(Path.Combine(_directory, "unused.db")));
    }

    [Fact]
    public async Task Provider_reports_storage_failure_without_swallowing_cancellation()
    {
        var store = new Mock<ISceneMemoryStore>(MockBehavior.Strict);
        store.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Storage offline"));
        var provider = new SceneMemoryProvider(store.Object);
        var health = await provider.GetHealthAsync();
        Assert.Equal(RagHealthStatus.Unavailable, health.Status);
        Assert.Contains("Storage offline", health.Message);
        store.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetHealthAsync());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.QueryAsync(new("workspace", "CupA"), cts.Token));
    }

    [Fact]
    public async Task Tool_uses_injected_provider_and_accepts_rag_style_query_name()
    {
        var provider = new Mock<ISceneMemoryProvider>(MockBehavior.Strict);
        using var cts = new CancellationTokenSource();
        var request = new SceneMemoryQueryRequest("workspace", "CupA", AtSequence: 2);
        var expected = new SceneQueryResult("workspace", 2, "no_facts", "No facts", Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        provider.Setup(p => p.QueryAsync(request, cts.Token)).ReturnsAsync(expected);
        var tool = new SceneMemoryQueryTool("test", provider.Object);
        var response = await tool.ReceiveAsync(new MessageEnvelope(new ToolRequest
        {
            Operation = "Query", Parameters = new Dictionary<string, object>
            { ["workspaceId"] = "workspace", ["query"] = "CupA", ["atSequence"] = 2 }
        }), cts.Token);
        var result = Assert.IsType<ToolResult>(response.Payload);
        Assert.True(result.IsSuccess);
        Assert.Same(expected, result.Output);
        provider.VerifyAll();
    }

    [Fact]
    public async Task State_survives_reopening_and_history_preserves_previous_values()
    {
        var store = Store();
        var workspace = await store.CreateAsync("Kitchen story");
        workspace = await store.AddEntityAsync(workspace.Id, "CupA", "Object");
        var cup = workspace.Entities.Single();
        await store.RecordAsync(workspace.Id, cup.Id, "Location", "On the table", "Clip 1: ActorA puts CupA down");
        await store.RecordAsync(workspace.Id, cup.Id, "location", "Held by ActorB", "Clip 2: ActorB picks up CupA");
        store = Store();

        var current = await store.QueryAsync(workspace.Id, new SceneQuery(Question: "Where is CupA?"));
        Assert.Equal("found", current.Status);
        Assert.Equal("Held by ActorB", Assert.Single(current.Facts).Value);
        Assert.Equal(2, current.Facts.Single().Sequence);
        var past = await store.QueryAsync(workspace.Id, new SceneQuery(cup.Id, AtSequence: 1));
        Assert.Equal("On the table", Assert.Single(past.Facts).Value);
        Assert.Equal("Clip 1: ActorA puts CupA down", past.Facts.Single().Note);
        Assert.Equal(2, (await store.GetAsync(workspace.Id)).History.Count);
        Assert.Equal("no_facts", (await store.QueryAsync(workspace.Id, new SceneQuery(cup.Id, AtSequence: 0))).Status);
    }

    [Fact]
    public async Task Offscreen_entities_retain_state_and_notes_do_not_change_other_entities()
    {
        var store = Store();
        var workspace = await store.CreateAsync("Scene");
        workspace = await store.AddEntityAsync(workspace.Id, "CupA", "Object");
        workspace = await store.AddEntityAsync(workspace.Id, "ActorB", "Character");
        var cup = workspace.Entities[0]; var actor = workspace.Entities[1];
        await store.RecordAsync(workspace.Id, cup.Id, "location", "Kitchen table", null);
        await store.RecordAsync(workspace.Id, actor.Id, "location", "Garden", "Camera follows ActorB; CupA is supposedly broken");
        await store.RecordAsync(workspace.Id, actor.Id, "location", "Kitchen", "Camera returns");
        var result = await store.QueryAsync(workspace.Id, new SceneQuery(cup.Id));
        Assert.Equal("Kitchen table", Assert.Single(result.Facts).Value);
        Assert.Equal(1, result.Facts.Single().Sequence);
        Assert.Equal(3, result.AtSequence);
    }

    [Fact]
    public async Task Workspace_boundaries_and_unknown_or_ambiguous_questions_do_not_guess()
    {
        var store = Store();
        var a = await store.CreateAsync("A"); var b = await store.CreateAsync("B");
        a = await store.AddEntityAsync(a.Id, "CupA", "Object");
        a = await store.AddEntityAsync(a.Id, "CupB", "Object");
        await store.RecordAsync(a.Id, a.Entities[0].Id, "location", "Table", null);
        Assert.Equal("not_found", (await store.QueryAsync(b.Id, new SceneQuery(a.Entities[0].Id))).Status);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RecordAsync(b.Id, a.Entities[0].Id, "location", "Elsewhere", null));
        Assert.Equal("not_found", (await store.QueryAsync(a.Id, new SceneQuery(Question: "CupAB"))).Status);
        var ambiguous = await store.QueryAsync(a.Id, new SceneQuery(Question: "CupA and CupB"));
        Assert.Equal("ambiguous", ambiguous.Status);
        Assert.Empty(ambiguous.Facts);
        Assert.Equal(2, ambiguous.Matches.Count);
        Assert.Equal("found", (await store.QueryAsync(a.Id, new SceneQuery(Question: "where is cupa?"))).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddEntityAsync(a.Id, " cupa ", "Object"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.QueryAsync(a.Id, new SceneQuery(a.Entities[0].Id, AtSequence: 2)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.QueryAsync(a.Id, new SceneQuery(a.Entities[0].Id, AtSequence: -1)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddEntityAsync(a.Id, "X", "Unsupported"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RecordAsync(a.Id, a.Entities[0].Id, " ", "value", null));
    }

    [Fact]
    public async Task Independent_store_instances_do_not_lose_concurrent_changes()
    {
        var first = Store(); var second = Store();
        var workspace = await first.CreateAsync("Concurrent");
        workspace = await first.AddEntityAsync(workspace.Id, "MusicA", "Music");
        var entity = workspace.Entities.Single();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            (index % 2 == 0 ? first : second).RecordAsync(workspace.Id, entity.Id, "track", $"Track {index}", null))));
        var history = (await first.GetAsync(workspace.Id)).History;
        Assert.Equal(12, history.Count);
        Assert.Equal(Enumerable.Range(1, 12).Select(i => (long)i), history.Select(f => f.Sequence));
        Assert.Equal(12, history.Select(f => f.Value).Distinct().Count());
    }

    [Fact]
    public async Task Tool_is_read_only_and_matches_direct_queries()
    {
        var store = Store();
        var workspace = await store.CreateAsync("Tools");
        workspace = await store.AddEntityAsync(workspace.Id, "MusicA", "Music");
        await store.RecordAsync(workspace.Id, workspace.Entities.Single().Id, "track", "theme.wav", null);
        var tool = new SceneMemoryQueryTool("test", store);
        var parameters = new Dictionary<string, object> { ["workspaceId"] = workspace.Id, ["question"] = "MusicA" };
        var result = await tool.Handle(new ToolRequest { Operation = "Query", Parameters = parameters });
        Assert.True(result.IsSuccess, result.Error);
        var viaTool = Assert.IsType<SceneQueryResult>(result.Output);
        Assert.Equal((await store.QueryAsync(workspace.Id, new SceneQuery(Question: "MusicA"))).Answer, viaTool.Answer);
        Assert.False((await tool.Handle(new ToolRequest { Operation = "Delete", Parameters = parameters })).IsSuccess);
        parameters["atSequence"] = "oops";
        Assert.False((await tool.Handle(new ToolRequest { Operation = "Query", Parameters = parameters })).IsSuccess);
        Assert.False((await tool.Handle(new ToolRequest { Operation = "Query", Parameters = null! })).IsSuccess);
        Assert.Single((await store.GetAsync(workspace.Id)).History);
    }

    [Fact]
    public async Task Test_agent_binds_workspace_and_calls_registered_tool_with_cancellation()
    {
        var factory = new Mock<IAgentFactory>();
        ToolRequest? captured = null;
        using var cts = new CancellationTokenSource();
        var expected = new ToolResult { IsSuccess = true, Output = "test facts" };
        factory.Setup(f => f.InvokeToolRequestAsync(SceneMemoryTool.TypeName, It.IsAny<ToolRequest>(), "agent", cts.Token))
            .Callback<string, ToolRequest, string?, CancellationToken>((_, request, _, _) => captured = request)
            .ReturnsAsync(expected);
        var agent = new SceneMemoryTestAgent("agent", "Test", "fixed-workspace", factory.Object);
        var response = await agent.ReceiveAsync(new MessageEnvelope(new SceneQuery(Question: "CupA", AtSequence: 3)), cts.Token);
        Assert.Same(expected, response.Payload);
        Assert.NotNull(captured);
        Assert.Equal("Query", captured.Operation);
        Assert.Equal("fixed-workspace", captured.Parameters["workspaceId"]);
        Assert.Equal("CupA", captured.Parameters["question"]);
        Assert.Equal(3L, captured.Parameters["atSequence"]);
        factory.VerifyAll();
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
