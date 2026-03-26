using AgctorSDK.Core.Interfaces;
using AgctorSDK.Host.Services;
using Moq;
using Xunit;
using FluentAssertions;

namespace AgctorSDK.Host.IntegrationTests;

public class RuntimeSelectionNormalizerTests
{
    [Fact]
    public void TryNormalize_Proto_Alias_To_ProtoActor()
    {
        var factory = new Mock<IActorRuntimeAdapterFactory>();
        factory.Setup(f => f.GetAvailableRuntimes()).Returns(new[] { "InMemory", "Proto.Actor", "Orleans" });

        var ok = RuntimeSelectionNormalizer.TryNormalize("Proto", factory.Object, out var c, out var err);
        ok.Should().BeTrue();
        err.Should().BeNull();
        c.Should().Be("Proto.Actor");
    }

    [Fact]
    public void TryNormalize_Empty_Fails()
    {
        var factory = new Mock<IActorRuntimeAdapterFactory>();
        factory.Setup(f => f.GetAvailableRuntimes()).Returns(Array.Empty<string>());

        var ok = RuntimeSelectionNormalizer.TryNormalize(" ", factory.Object, out _, out var err);
        ok.Should().BeFalse();
        err.Should().Contain("required");
    }
}
