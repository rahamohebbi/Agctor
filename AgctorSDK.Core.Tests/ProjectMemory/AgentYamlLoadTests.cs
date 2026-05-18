using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Yaml;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

/// <summary>Guards sample <c>*.agent.yaml</c> files deserialize (unquoted colons in instructions break YamlDotNet).</summary>
public sealed class AgentYamlLoadTests
{
    private static string SampleRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));

    [Fact]
    public void SampleProject_AllAgentYamlFiles_Deserialize()
    {
        var agentsDir = Path.Combine(SampleRoot, ".agctor", "agents");
        Directory.Exists(agentsDir).Should().BeTrue("sample project must exist at {0}", SampleRoot);

        foreach (var file in Directory.EnumerateFiles(agentsDir, "*.agent.yaml", SearchOption.AllDirectories))
        {
            var act = () => ProjectYamlSerializer.DeserializeFromFile<AgentDefinitionSpec>(file);
            act.Should().NotThrow("file {0} must deserialize", file);
        }
    }

    [Fact]
    public async Task SampleProject_ProjectLoader_IncludesRelationshipCoach()
    {
        var loader = new ProjectLoader();
        var ctx = await loader.LoadAsync(SampleRoot);
        ctx.AgentSpecs.Select(a => a.Id).Should().Contain("relationship-coach");
    }
}
