using System.Text.Json.Nodes;
using AgctorSDK.Host.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using FluentAssertions;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// PRD-012: appsettings.User.json merge for runtime keys.
/// </summary>
public class UserRuntimeSettingsServiceTests
{
    [Fact]
    public async Task PersistAsync_Merges_And_Preserves_Other_Agctor_Keys()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agctor-urt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var userPath = Path.Combine(dir, "appsettings.User.json");
        var root = new JsonObject
        {
            ["Agctor"] = new JsonObject
            {
                ["AgentTypeEnablement"] = new JsonObject { ["LLMAgent"] = false },
                ["Other"] = "keep"
            }
        };
        await File.WriteAllTextAsync(userPath, root.ToJsonString());

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(dir);
        var svc = new UserRuntimeSettingsService(env.Object, NullLogger<UserRuntimeSettingsService>.Instance);

        await svc.PersistAsync(new RuntimeSettingsUpdate
        {
            CanonicalRuntimeId = "Orleans",
            ProtoHost = "10.0.0.5",
            ProtoPort = 13000,
            AllowExperimentalRuntimes = true,
            OrleansClusterId = "agctor-dev",
            OrleansGatewayPort = 30000
        });

        var after = JsonNode.Parse(await File.ReadAllTextAsync(userPath))!.AsObject();
        var agctor = after["Agctor"]!.AsObject();
        agctor["DefaultRuntime"]!.GetValue<string>().Should().Be("Orleans");
        agctor["ProtoHost"]!.GetValue<string>().Should().Be("10.0.0.5");
        agctor["OrleansClusterId"]!.GetValue<string>().Should().Be("agctor-dev");
        agctor["OrleansGatewayPort"]!.GetValue<int>().Should().Be(30000);
        agctor["AllowExperimentalRuntimes"]!.GetValue<bool>().Should().BeTrue();
        agctor["Other"]!.GetValue<string>().Should().Be("keep");
        agctor["AgentTypeEnablement"]!.AsObject()["LLMAgent"]!.GetValue<bool>().Should().BeFalse();

        await svc.PersistAsync(new RuntimeSettingsUpdate
        {
            CanonicalRuntimeId = "Orleans",
            ProtoHost = "10.0.0.5",
            ProtoPort = 13000,
            AllowExperimentalRuntimes = true,
            OrleansClusterId = "agctor-dev",
            OrleansGatewayPort = 30000
        });

        await svc.PersistAsync(new RuntimeSettingsUpdate { CanonicalRuntimeId = "Proto.Actor" });

        var afterSwitch = JsonNode.Parse(await File.ReadAllTextAsync(userPath))!.AsObject();
        var agctorAfter = afterSwitch["Agctor"]!.AsObject();
        agctorAfter["DefaultRuntime"]!.GetValue<string>().Should().Be("Proto.Actor");
        agctorAfter["ProtoHost"]!.GetValue<string>().Should().Be("10.0.0.5");
        agctorAfter["AllowExperimentalRuntimes"]!.GetValue<bool>().Should().BeTrue();

        try
        {
            Directory.Delete(dir, true);
        }
        catch
        {
            // best-effort cleanup on temp
        }
    }
}
