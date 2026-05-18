using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Tools;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class PersonMemoryMarkdownContextBuilderTests
{
    [Fact]
    public void ExtractFocusQueryFromUserMessage_Parses_Quoted_Name()
    {
        PersonMemoryMarkdownContextBuilder.ExtractFocusQueryFromUserMessage("Who is \"Jane Doe\" here?")
            .Should().Be("Jane Doe");
    }

    [Fact]
    public void ParseStrategy_Defaults_When_Config_Null()
    {
        PersonMemoryMarkdownContextBuilder.ParseStrategy(null).Should().Be("markdown_all");
    }

    [Fact]
    public void ParseStrategy_Reads_ContextStrategy_From_Json()
    {
        var el = JsonDocument.Parse("""{"contextStrategy":"markdown_focus"}""").RootElement;
        PersonMemoryMarkdownContextBuilder.ParseStrategy(el).Should().Be("markdown_focus");
    }

    [Fact]
    public void ExtractFocusQueryFromUserMessage_Parses_Possessive_Name()
    {
        PersonMemoryMarkdownContextBuilder.ExtractFocusQueryFromUserMessage("I am Ryan's dad what is important?")
            .Should().Be("Ryan");
    }
}
