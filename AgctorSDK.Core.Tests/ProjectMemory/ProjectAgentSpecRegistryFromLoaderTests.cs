using System.IO;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class ProjectAgentSpecRegistryFromLoaderTests
{
    [Fact]
    public async Task GetAllAsync_EmptyProjectRoot_ReturnsEmpty()
    {
        var opt = Options.Create(new ProjectMemoryAgentOptions { ProjectRoot = "" });
        var monitor = Mock.Of<IOptionsMonitor<ProjectMemoryAgentOptions>>(m => m.CurrentValue == opt.Value);
        var loader = new Mock<IProjectLoader>(MockBehavior.Strict);
        var reg = new ProjectAgentSpecRegistryFromLoader(monitor, loader.Object, NullLogger<ProjectAgentSpecRegistryFromLoader>.Instance);

        var all = await reg.GetAllAsync();

        all.Should().BeEmpty();
        loader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAllAsync_LoadsFromLoader_WhenRootSet()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "agctor-pm-registry-test-" + Guid.NewGuid().ToString("N")));
        var opt = Options.Create(new ProjectMemoryAgentOptions { ProjectRoot = root });
        var monitor = Mock.Of<IOptionsMonitor<ProjectMemoryAgentOptions>>(m => m.CurrentValue == opt.Value);
        var spec = new AgentDefinitionSpec { Id = "a1", Name = "A" };
        var ctx = new LoadedProjectContext { AgentSpecs = new List<AgentDefinitionSpec> { spec } };
        var loader = new Mock<IProjectLoader>();
        loader.Setup(l => l.LoadAsync(root, It.IsAny<CancellationToken>())).ReturnsAsync(ctx);

        var reg = new ProjectAgentSpecRegistryFromLoader(monitor, loader.Object, NullLogger<ProjectAgentSpecRegistryFromLoader>.Instance);
        var all = await reg.GetAllAsync();

        all.Should().ContainSingle().Which.Id.Should().Be("a1");
    }
}
