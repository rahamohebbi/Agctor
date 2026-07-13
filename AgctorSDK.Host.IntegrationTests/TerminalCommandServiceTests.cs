using AgctorSDK.Host.Services;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>Validates terminal command building and safety checks for the dashboard panel.</summary>
public class TerminalCommandServiceTests
{
    private readonly TerminalCommandService _svc;

    public TerminalCommandServiceTests()
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AgctorSDK.Host")));
        var actorDocker = new ActorRuntimeDockerService(env.Object, NullLogger<ActorRuntimeDockerService>.Instance);
        var ragDocker = new RagProviderDockerService(env.Object, NullLogger<RagProviderDockerService>.Instance);
        _svc = new TerminalCommandService(env.Object, actorDocker, ragDocker, NullLogger<TerminalCommandService>.Instance);
    }

    [Fact]
    public void Orleans_presets_include_compose_up_command()
    {
        var presets = _svc.GetPresets("actor-runtime", "Orleans");
        presets.Should().NotBeEmpty();
        presets.Should().Contain(p => p.Command.Contains("orleans-silo", StringComparison.OrdinalIgnoreCase));
        presets.Should().Contain(p => p.Command.Contains("docker compose -f docker/actor-runtimes/docker-compose.yml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryValidate_rejects_shell_chaining()
    {
        _svc.TryValidate("docker compose -f docker/actor-runtimes/docker-compose.yml up -d orleans-silo; rm -rf /", out var err).Should().BeFalse();
        err.Should().Contain("disallowed");
    }

    [Fact]
    public void TryValidate_accepts_orleans_start_command()
    {
        var cmd = _svc.GetDefaultCommand("actor-runtime", "Orleans");
        _svc.TryValidate(cmd, out var err).Should().BeTrue(err);
    }

    [Fact]
    public void TryValidate_rejects_non_docker_commands()
    {
        _svc.TryValidate("curl http://example.com", out _).Should().BeFalse();
    }

    [Fact]
    public void LightRag_presets_use_rag_compose_path()
    {
        var presets = _svc.GetPresets("rag-provider", "LightRAG");
        presets.Should().NotBeEmpty();
        presets.Should().Contain(p => p.Command.Contains("lightrag", StringComparison.OrdinalIgnoreCase));
        presets.Should().Contain(p => p.Command.Contains("docker/rag-providers/docker-compose.yml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Graphiti_presets_use_rag_compose_path()
    {
        var presets = _svc.GetPresets("rag-provider", "Graphiti");
        presets.Should().NotBeEmpty();
        presets.Should().Contain(p => p.Command.Contains("graphiti", StringComparison.OrdinalIgnoreCase));
        presets.Should().Contain(p => p.Command.Contains("docker/rag-providers/docker-compose.yml", StringComparison.OrdinalIgnoreCase));
    }
}
