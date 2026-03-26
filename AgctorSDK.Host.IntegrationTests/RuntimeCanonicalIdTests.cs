using AgctorSDK.Core.Adapters;
using AgctorSDK.Host.Services;
using Xunit;
using FluentAssertions;

namespace AgctorSDK.Host.IntegrationTests;

public class RuntimeCanonicalIdTests
{
    [Fact]
    public void FromAdapter_InMemory_Maps_To_Factory_Key()
    {
        var rt = new InMemoryActorRuntime();
        RuntimeCanonicalId.FromAdapter(rt).Should().Be("InMemory");
    }

    [Fact]
    public void FromAdapter_Proto_Maps_To_ProtoActor()
    {
        var rt = new ProtoActorAdapter();
        RuntimeCanonicalId.FromAdapter(rt).Should().Be("Proto.Actor");
    }

    [Fact]
    public void FromAdapter_Orleans_Maps_To_Orleans()
    {
        var rt = new OrleansAdapter();
        RuntimeCanonicalId.FromAdapter(rt).Should().Be("Orleans");
    }
}
