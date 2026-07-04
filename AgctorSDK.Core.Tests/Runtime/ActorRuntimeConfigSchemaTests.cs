using AgctorSDK.Core.Runtime;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Runtime;

public class ActorRuntimeConfigSchemaTests
{
    [Theory]
    [InlineData("InMemory", false)]
    [InlineData("Orleans", true)]
    [InlineData("Proto.Actor", true)]
    public void RequiresDocker_matches_runtime(string id, bool expected)
        => ActorRuntimeConfigSchema.DockerBackedRuntimes.Contains(id).Should().Be(expected);

    [Fact]
    public void Orleans_fields_include_cluster_and_gateway()
    {
        var fields = ActorRuntimeConfigSchema.GetFields("Orleans");
        fields.Select(f => f.Key).Should().Contain(new[] { "OrleansClusterId", "OrleansGatewayPort", "AllowExperimentalRuntimes" });
    }
}
