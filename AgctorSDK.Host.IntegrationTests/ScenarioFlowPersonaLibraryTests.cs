using System.Text.Json;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenarioFlowPersonaLibraryTests
{
    [Theory]
    [InlineData("diagram-creator", true)]
    [InlineData("missing-agent", false)]
    public async Task SavingFlow_AttachesLibraryPersonaAtomically_AndRejectsUnknownPersona(string personaId, bool expectedOk)
    {
        var root = Path.Combine(Path.GetTempPath(), "agctor-flow-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = Options.Create(new ScenarioCatalogOptions { DefaultFile = "defaults.json", UserFile = "user.json" });
            var env = Mock.Of<IWebHostEnvironment>(e => e.ContentRootPath == root);
            var registry = new ProjectAgentSpecRegistry([
                new AgentDefinitionSpec { Id = "diagram-creator", Name = "Create Diagram" },
                new AgentDefinitionSpec { Id = "existing", Name = "Existing agent" }
            ]);
            var catalog = new JsonScenarioCatalog(env, options, Options.Create(new AgentTypeOptions()), registry, NullLogger<JsonScenarioCatalog>.Instance);
            (await catalog.CreateScenarioAsync(new ScenarioDefinition { Id = "test", RagProviderId = "Graphiti", PersonaAgentIds = ["existing"] })).Ok.Should().BeTrue();
            (await catalog.CreateScenarioAsync(new ScenarioDefinition { Id = "invalid", RagProviderId = "unknown" })).Ok.Should().BeFalse();
            var before = await File.ReadAllTextAsync(Path.Combine(root, "user.json"));
            var flow = new ScenarioFlowDocument
            {
                GraphId = "test",
                Nodes = [
                    new() { Id = "input", Type = "ChatInput" },
                    new() { Id = "create", Type = "LlmNode", Config = JsonSerializer.SerializeToElement(new { personaId }) },
                    new() { Id = "output", Type = "Output" }
                ],
                Edges = [
                    new() { Id = "e1", FromNodeId = "input", ToNodeId = "create", Mode = "sequential" },
                    new() { Id = "e2", FromNodeId = "create", ToNodeId = "output", Mode = "sequential" }
                ]
            };
            var saved = await catalog.SaveScenarioFlowAsync("test", flow);
            saved.Ok.Should().Be(expectedOk, string.Join("; ", saved.Errors));
            if (expectedOk)
            {
                (await catalog.SaveScenarioFlowAsync("test", flow)).Ok.Should().BeTrue();
                await catalog.ReloadAsync();
                catalog.Get("test")!.RagProviderId.Should().Be("Graphiti");
                catalog.Get("test")!.PersonaAgentIds.Should().BeEquivalentTo(["existing", "diagram-creator"]);
                catalog.Get("test")!.Flow!.Nodes.Single(n => n.Id == "create").Config!.Value.GetProperty("personaId").GetString().Should().Be(personaId);
            }
            else
            {
                saved.Errors.Should().Contain(e => e.Contains("unknown persona agent id"));
                (await File.ReadAllTextAsync(Path.Combine(root, "user.json"))).Should().Be(before);
                catalog.Get("test")!.Flow.Should().BeNull();
                catalog.Get("test")!.PersonaAgentIds.Should().Equal("existing");
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
