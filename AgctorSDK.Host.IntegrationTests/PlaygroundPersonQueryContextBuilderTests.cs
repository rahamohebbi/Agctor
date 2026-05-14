using AgctorSDK.Host.Services.ProjectMemory;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class PlaygroundPersonQueryContextBuilderTests
{
    [Theory]
    [InlineData("Who is Raha?", "Raha")]
    [InlineData("who is raha?", "raha")]
    [InlineData("Tell me about Melody Smith", "Melody")]
    [InlineData("What is Raha?", "Raha")]
    public void ExtractFocusQueryFromUserMessage_heuristic(string input, string expected)
    {
        PlaygroundPersonQueryContextBuilder.ExtractFocusQueryFromUserMessage(input).Should().Be(expected);
    }

    [Fact]
    public void ExtractFocusQueryFromUserMessage_quoted()
    {
        PlaygroundPersonQueryContextBuilder.ExtractFocusQueryFromUserMessage("Who is \"Jane Doe\" here?")
            .Should().Be("Jane Doe");
    }

    [Fact]
    public void ParseStrategy_defaults_to_markdown_all()
    {
        PlaygroundPersonQueryContextBuilder.ParseStrategy(null).Should().Be("markdown_all");
    }
}
