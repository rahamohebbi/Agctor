using AgctorSDK.Core.Sessions;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Sessions;

public sealed class ChatProjectSettingsTests
{
    [Fact]
    public void ResolveVisualMaxPhotos_uses_project_override_then_default()
    {
        ChatProjectSettings.ResolveVisualMaxPhotos(5, defaultValue: 3, maxCap: 12).Should().Be(5);
        ChatProjectSettings.ResolveVisualMaxPhotos(null, defaultValue: 3, maxCap: 12).Should().Be(3);
    }

    [Fact]
    public void ClampVisualMaxPhotos_enforces_bounds()
    {
        ChatProjectSettings.ClampVisualMaxPhotos(0).Should().Be(1);
        ChatProjectSettings.ClampVisualMaxPhotos(99, maxCap: 12).Should().Be(12);
    }

    [Fact]
    public void ToJson_round_trips_visualMaxPhotos()
    {
        var settings = new ChatProjectSettings { VisualMaxPhotos = 7 };
        var loaded = ChatProjectSettings.FromJson(settings.ToJson());
        loaded.VisualMaxPhotos.Should().Be(7);
    }
}
