using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Parsing;

public sealed class ParsedMarkdownDocument
{
    public string Raw { get; init; } = "";
    public List<MarkdownSection> Sections { get; init; } = new();
}

public sealed class MarkdownSection
{
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public int StartLineIndex { get; init; }
}
