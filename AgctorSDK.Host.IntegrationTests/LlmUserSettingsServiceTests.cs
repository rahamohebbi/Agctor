using System.Text.Json.Nodes;
using AgctorSDK.Host.Services;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>PRD-015: default model is written to appsettings.json and appsettings.User.json.</summary>
public class LlmUserSettingsServiceTests
{
    [Fact]
    public async Task PersistDefaultModelAsync_Updates_AppSettings_And_User_File()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agctor-llm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var appPath = Path.Combine(dir, "appsettings.json");
        var userPath = Path.Combine(dir, "appsettings.User.json");
        var root = new JsonObject
        {
            ["Agctor"] = new JsonObject
            {
                ["LLM"] = new JsonObject
                {
                    ["DefaultModel"] = "old-model",
                    ["VisionModel"] = "gemma4:31b"
                },
                ["DefaultRuntime"] = "InMemory"
            }
        };
        await File.WriteAllTextAsync(appPath, root.ToJsonString());

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(dir);
        var svc = new LlmUserSettingsService(env.Object, NullLogger<LlmUserSettingsService>.Instance);

        await svc.PersistDefaultModelAsync("qwen3.5:9b");

        var appAfter = JsonNode.Parse(await File.ReadAllTextAsync(appPath))!.AsObject();
        appAfter["Agctor"]!.AsObject()["LLM"]!.AsObject()["DefaultModel"]!.GetValue<string>()
            .Should().Be("qwen3.5:9b");
        appAfter["Agctor"]!.AsObject()["LLM"]!.AsObject()["VisionModel"]!.GetValue<string>()
            .Should().Be("gemma4:31b");

        File.Exists(userPath).Should().BeTrue();
        var userAfter = JsonNode.Parse(await File.ReadAllTextAsync(userPath))!.AsObject();
        userAfter["Agctor"]!.AsObject()["LLM"]!.AsObject()["DefaultModel"]!.GetValue<string>()
            .Should().Be("qwen3.5:9b");

        try
        {
            Directory.Delete(dir, true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
