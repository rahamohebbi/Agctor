using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Host;

/// <summary>
/// Documents default enablement semantics (missing or empty override means enabled) used by Host PRD-010.
/// </summary>
public class AgentTypeEnablementEffectiveTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void EffectiveEnabled_FromOptionalConfigString(string? raw, bool expected)
    {
        bool effective;
        if (string.IsNullOrEmpty(raw) || !bool.TryParse(raw, out var b))
            effective = true;
        else
            effective = b;

        effective.Should().Be(expected);
    }
}
