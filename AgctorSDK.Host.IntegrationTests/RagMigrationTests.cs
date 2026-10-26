using System.Text.Json;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.Rag;
using AgctorSDK.Host.Services.Rag;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class RagMigrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rag-migration-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IRagProviderAdapter> _adapter = new();
    private readonly Mock<IRagProviderAdapterFactory> _factory = new();
    private readonly Mock<IScenarioCatalog> _catalog = new();
    private readonly RagOptions _options = new();
    private RagMigrationService _service = null!;
    private static IOptionsMonitor<T> Monitor<T>(T options) where T : class => Mock.Of<IOptionsMonitor<T>>(m => m.CurrentValue == options);
    private RagMigrationService Create() => new(_factory.Object, Monitor(new ProjectMemoryAgentOptions { ProjectRoot = _root }), Monitor(_options), _catalog.Object, Path.Combine(_root, "jobs"));
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _catalog.Setup(c => c.Get("alpha")).Returns(new ScenarioDefinition { Id = "alpha" });
        _factory.Setup(f => f.IsProviderAvailable(It.IsAny<string>())).Returns(true);
        _factory.Setup(f => f.CreateProvider(It.IsAny<string>())).Returns(_adapter.Object);
        _adapter.Setup(a => a.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RagHealthResult(RagHealthStatus.Healthy, "ok"));
        _adapter.Setup(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RagIngestResult(true, "queued"));
        _service = Create();
        return Task.CompletedTask;
    }
    private void Write(string path, string text = "# Sample memory\nAlex enjoys hiking.")
    { var full = Path.Combine(_root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, text); }
    private Task<RagMigrationJob> Preview(string provider = "Graphiti", bool force = false) => _service.PreviewAsync("alpha", provider, "scenario", force, default);
    private async Task<RagMigrationJob> Run(RagMigrationJob preview)
    {
        _service.Start(preview.Id, preview.Files.Where(f => f.Status == "pending").Select(f => f.Path).ToArray());
        await _service.StartAsync(default);
        return await Finished(preview.Id);
    }
    private async Task<RagMigrationJob> Finished(string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var job = _service.Get(id)!;
            if (job.Status is not ("queued" or "running")) return job;
            await Task.Delay(10, timeout.Token);
        }
    }
    [Fact]
    public async Task Preview_IsolatesScenario_ExcludesLinksAndEmptyFiles_AndSendsNothing()
    {
        Write("scenarios/alpha/people/alex/profile.md"); Write("scenarios/beta/people/sam/profile.md");
        Write(".agctor/templates/profile.md"); Write("scenarios/alpha/people/empty.md", " ");
        Write("scenarios/alpha/people/large.md", new string('a', 512001));
        File.CreateSymbolicLink(Path.Combine(_root, "scenarios/alpha/linked.md"), Path.Combine(_root, "scenarios/beta/people/sam/profile.md"));
        var preview = await Preview();
        preview.Files.Count(f => f.Status == "pending").Should().Be(1);
        preview.Files.Count(f => f.Status == "excluded").Should().Be(3);
        preview.Files.Should().OnlyContain(f => f.Path.StartsWith("scenarios/alpha/"));
        _adapter.Verify(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task AcceptedFiles_ArePreservedAndSkippedAcrossRestart_ButChangesAndNewDestinationAreNot()
    {
        const string path = "scenarios/alpha/people/alex/profile.md"; Write(path);
        var original = File.ReadAllText(Path.Combine(_root, path));
        var result = await Run(await Preview());
        result.Status.Should().Be("completed"); result.Files.Single().Status.Should().Be("accepted");
        File.ReadAllText(Path.Combine(_root, path)).Should().Be(original);
        await _service.StopAsync(default); _service.Dispose(); _service = Create();
        (await Preview()).Files.Single().Status.Should().Be("unchanged");
        (await Preview(force: true)).Files.Single().Status.Should().Be("pending");
        _options.Graphiti.BaseUrl = "http://another-provider:8001";
        (await Preview()).Files.Single().Status.Should().Be("pending");
        _options.Graphiti.BaseUrl = "http://127.0.0.1:8001";
        Write(path, "Updated memory");
        (await Preview()).Files.Single().Status.Should().Be("pending");
    }
    [Fact]
    public async Task ChangedAfterPreview_IsNotSent_AndUnselectedFilesAreNotSent()
    {
        Write("scenarios/alpha/one.md"); Write("scenarios/alpha/two.md");
        var preview = await Preview(); Write("scenarios/alpha/one.md", "changed");
        _service.Start(preview.Id, ["scenarios/alpha/one.md"]); await _service.StartAsync(default);
        var job = await Finished(preview.Id);
        job.Status.Should().Be("partial");
        job.Files.Single(f => f.Path.EndsWith("one.md")).Status.Should().Be("failed");
        job.Files.Single(f => f.Path.EndsWith("two.md")).Status.Should().Be("excluded");
        _adapter.Verify(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task InvalidSelectionsAndProviderChanges_RejectStart()
    {
        Write("scenarios/alpha/one.md"); var preview = await Preview();
        var outside = () => _service.Start(preview.Id, ["../outside.md"]);
        outside.Should().Throw<ArgumentException>();
        _options.Graphiti.BaseUrl = "http://changed:8001";
        var changed = () => _service.Start(preview.Id, ["scenarios/alpha/one.md"]);
        changed.Should().Throw<ArgumentException>().WithMessage("*settings changed*");
    }
    [Theory]
    [InlineData("Graphiti", 2)]
    [InlineData("LightRAG", 2)]
    [InlineData("Cognee", 1)]
    public async Task ProviderSubmission_UsesScenarioCollection_AndCogneeBatches(string provider, int calls)
    {
        Write("scenarios/alpha/one.md"); Write("scenarios/alpha/two.md");
        var result = await Run(await Preview(provider));
        result.Files.Should().OnlyContain(f => f.Status == "accepted");
        _adapter.Verify(a => a.IngestAsync(It.Is<RagIngestRequest>(r => r.CollectionId == "alpha" && r.Content!.Contains("Sample memory")), It.IsAny<CancellationToken>()), Times.Exactly(calls));
        if (provider == "Cognee") _adapter.Verify(a => a.IngestAsync(It.Is<RagIngestRequest>(r => r.Content!.Contains("scenarios/alpha/one.md") && r.Content.Contains("scenarios/alpha/two.md")), It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task OfflineProvider_SendsNothing_AndRemainsRetryable()
    {
        Write("scenarios/alpha/one.md");
        _adapter.Setup(a => a.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RagHealthResult(RagHealthStatus.Unavailable, "offline"));
        var result = await Run(await Preview()); result.Status.Should().Be("failed");
        (await Preview()).Files.Single().Status.Should().Be("pending");
        _adapter.Verify(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task PartialFailure_RetryOnlyIncludesUnacceptedFiles()
    {
        Write("scenarios/alpha/one.md"); Write("scenarios/alpha/two.md");
        _adapter.SetupSequence(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagIngestResult(true, "ok")).ReturnsAsync(new RagIngestResult(false, "failed"));
        (await Run(await Preview())).Status.Should().Be("partial");
        var retry = await Preview();
        retry.Files.Count(f => f.Status == "unchanged").Should().Be(1);
        retry.Files.Count(f => f.Status == "pending").Should().Be(1);
    }
    [Fact]
    public async Task Stop_FinishesInFlightRequestWithoutSendingNextFile()
    {
        Write("scenarios/alpha/one.md"); Write("scenarios/alpha/two.md");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<RagIngestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _adapter.Setup(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>())).Returns(() => { entered.TrySetResult(); return release.Task; });
        var preview = await Preview();
        _service.Start(preview.Id, preview.Files.Select(f => f.Path).ToArray()); await _service.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _service.Stop(preview.Id); release.SetResult(new RagIngestResult(true, "ok"));
        var result = await Finished(preview.Id);
        result.Status.Should().Be("stopped");
        result.Files.Count(f => f.Status == "accepted").Should().Be(1);
        result.Files.Count(f => f.Status == "pending").Should().Be(1);
    }
    [Fact]
    public async Task Restart_MarksInFlightSendUncertain_AndNeverReplaysIt()
    {
        Write("scenarios/alpha/one.md"); var job = await Preview();
        job.Status = "running"; job.Files[0].Status = "sending";
        File.WriteAllText(Path.Combine(_root, "jobs", job.Id + ".json"), JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        _service.Dispose(); _service = Create(); await _service.StartAsync(default);
        _service.Get(job.Id)!.Status.Should().Be("interrupted");
        _service.Get(job.Id)!.Files[0].Status.Should().Be("uncertain");
        _adapter.Verify(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task OldPreview_RechecksReceiptsBeforeSending()
    {
        Write("scenarios/alpha/one.md"); var older = await Preview();
        await Run(await Preview());
        _service.Start(older.Id, ["scenarios/alpha/one.md"]);
        (await Finished(older.Id)).Files.Single().Status.Should().Be("unchanged");
        _adapter.Verify(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task InterruptedSubmission_RequiresExplicitFileSelectionOnRetry()
    {
        Write("scenarios/alpha/one.md");
        _adapter.Setup(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Connection lost"));
        (await Run(await Preview())).Files.Single().Status.Should().Be("uncertain");
        (await Preview()).Files.Single().Status.Should().Be("review");
    }
    [Fact]
    public async Task LegacySource_IsOptIn_AndUsesDestinationScenarioCollection()
    {
        Write("people/legacy/profile.md"); Write("scenarios/alpha/new.md");
        var job = await _service.PreviewAsync("alpha", "Graphiti", "legacy", false, default);
        job.Files.Should().ContainSingle().Which.Path.Should().Be("people/legacy/profile.md");
        job.CollectionId.Should().Be("alpha");
    }
    [Fact]
    public async Task ReadinessChecksSelectedProvider_ReportsReasonWithoutSendingDocuments()
    {
        _adapter.Setup(a => a.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RagHealthResult(RagHealthStatus.Unavailable, "LightRAG /health returned HTTP 401."));
        var ready = await _service.CheckProviderAsync("LightRAG", default);
        ready.Ready.Should().BeFalse(); ready.Message.Should().Contain("401");
        _factory.Verify(f => f.CreateProvider("LightRAG"), Times.Once);
        _adapter.Verify(a => a.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Write("scenarios/alpha/one.md");
        var failed = await Run(await Preview("LightRAG"));
        failed.Message.Should().Contain("401").And.Contain("No files were sent");
    }
    [Fact]
    public async Task ReadinessRedactsCredentialsAndQueryStrings()
    {
        _options.LightRAG.ApiKey = "private-test-key";
        _adapter.Setup(a => a.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RagHealthResult(RagHealthStatus.Unavailable,
            "Failed private-test-key https://user:password@example.test/health?token=private-query"));
        var result = await _service.CheckProviderAsync("LightRAG", default);
        result.Message.Should().NotContain("private-test-key").And.NotContain("password").And.NotContain("private-query").And.Contain("example.test/health");
    }
    [Fact]
    public async Task ReadinessTransportFailure_HasRecoveryGuidance()
    {
        _adapter.Setup(a => a.GetHealthAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("connection refused"));
        (await _service.CheckProviderAsync("LightRAG", default)).Message.Should().Contain("Start the provider");
    }
    public async Task DisposeAsync()
    { await _service.StopAsync(default); _service.Dispose(); Directory.Delete(_root, true); }
}
