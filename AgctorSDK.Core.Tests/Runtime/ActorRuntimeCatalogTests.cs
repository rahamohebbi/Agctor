using AgctorSDK.Core.Runtime;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Runtime;

/// <summary>
/// PRD-012: catalog ids stay aligned with IActorRuntimeAdapterFactory keys.
/// </summary>
public class ActorRuntimeCatalogTests
{
    public static readonly string[] FactoryIds = { "InMemory", "Orleans", "Proto.Actor" };

    [Fact]
    public void All_Contains_Exactly_Factory_Ids()
    {
        var ids = ActorRuntimeCatalog.All.Select(a => a.Id).OrderBy(s => s).ToArray();
        ids.Should().Equal(FactoryIds.OrderBy(s => s).ToArray());
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("orleans")]
    [InlineData("PROTO.ACTOR")]
    public void GetById_Is_Case_Insensitive(string key)
    {
        var d = ActorRuntimeCatalog.GetById(key);
        d.Should().NotBeNull();
        d!.DisplayName.Should().NotBeNullOrWhiteSpace();
        d.Capabilities.Should().NotBeEmpty();
    }

    [Fact]
    public void GetById_Unknown_Returns_Null()
    {
        ActorRuntimeCatalog.GetById("wasmCloud").Should().BeNull();
    }

    [Theory]
    [InlineData("InMemory", "supported")]
    [InlineData("Proto.Actor", "experimental")]
    [InlineData("Orleans", "experimental")]
    public void Maturity_Reflects_Runtime_Conformance_Status(string id, string maturity)
    {
        ActorRuntimeCatalog.GetById(id)!.Maturity.Should().Be(maturity);
    }
}
