using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Extensions.Services;
using AgctorSDK.Host.Services;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class HostToolYamlMatcherTests
{
    private static AgctorToolCatalog.ToolCatalogEntry SampleEntry() =>
        new(
            PrimaryId: "person-memory-context",
            ClrTypeName: "PersonMemoryContextTool",
            ActorType: typeof(FileSystemTool),
            Discovery: new ToolInfo { Name = "Person memory", Description = "Reads memory" },
            ExposeOnHttpApi: true);

    [Fact]
    public void TokenMatchesHostTool_matches_http_primary_id()
    {
        HostToolYamlMatcher.TokenMatchesHostTool("person-memory-context", SampleEntry()).Should().BeTrue();
    }

    [Fact]
    public void TokenMatchesHostTool_matches_alphanumeric_clr_alias()
    {
        HostToolYamlMatcher.TokenMatchesHostTool("PersonMemoryContext", SampleEntry()).Should().BeTrue();
    }

    [Fact]
    public void IsKnownSemanticToken_recognizes_memory_intents_only()
    {
        HostToolYamlMatcher.IsKnownSemanticToken("memory_intents_only").Should().BeTrue();
        HostToolYamlMatcher.IsKnownSemanticToken("person-memory-context").Should().BeFalse();
    }
}
