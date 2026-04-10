using AgctorSDK.Core.ProjectMemory.Parsing;

namespace AgctorSDK.Core.ProjectMemory;

public interface IDocumentParser
{
    /// <summary>Split markdown on <c>##</c> headings (level-2 only for PRD templates).</summary>
    ParsedMarkdownDocument Parse(string markdownContent);
}
