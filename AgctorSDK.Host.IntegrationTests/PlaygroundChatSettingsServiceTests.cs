using System.Text.Json.Nodes;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Host.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Playground chat context cap persisted to appsettings.User.json (PRD-013).
/// </summary>
public class PlaygroundChatSettingsServiceTests
{
    [Fact]
    public async Task SaveAsync_Merges_MaxConversationTurns_And_Preserves_Other_Keys()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agctor-pcs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var userPath = Path.Combine(dir, "appsettings.User.json");
        var root = new JsonObject
        {
            ["Agctor"] = new JsonObject
            {
                ["DefaultRuntime"] = "InMemory",
                ["ProjectMemory"] = new JsonObject { ["ProjectRoot"] = "/tmp/sample" }
            }
        };
        await File.WriteAllTextAsync(userPath, root.ToJsonString());

        var config = new ConfigurationBuilder()
            .AddJsonFile(userPath, optional: false, reloadOnChange: true)
            .Build();

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(dir);
        var svc = new PlaygroundChatSettingsService(config, env.Object, NullLogger<PlaygroundChatSettingsService>.Instance);

        svc.GetMaxConversationTurns().Should().Be(PlaygroundChatSettings.DefaultMaxConversationTurns);

        var saved = await svc.SaveAsync(new PlaygroundChatSettingsUpdateDto { MaxConversationTurns = 40 });

        saved.MaxConversationTurns.Should().Be(40);
        saved.MinMaxConversationTurns.Should().Be(PlaygroundChatSettings.MinMaxConversationTurns);
        saved.MaxMaxConversationTurns.Should().Be(PlaygroundChatSettings.MaxMaxConversationTurns);
        svc.GetMaxConversationTurns().Should().Be(40);

        var after = JsonNode.Parse(await File.ReadAllTextAsync(userPath))!.AsObject();
        var agctor = after["Agctor"]!.AsObject();
        agctor["DefaultRuntime"]!.GetValue<string>().Should().Be("InMemory");
        agctor["ProjectMemory"]!.AsObject()["ProjectRoot"]!.GetValue<string>().Should().Be("/tmp/sample");
        agctor["ProjectMemory"]!.AsObject()["MaxConversationTurns"]!.GetValue<int>().Should().Be(40);

        var clamped = await svc.SaveAsync(new PlaygroundChatSettingsUpdateDto { MaxConversationTurns = 500 });
        clamped.MaxConversationTurns.Should().Be(PlaygroundChatSettings.MaxMaxConversationTurns);

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
