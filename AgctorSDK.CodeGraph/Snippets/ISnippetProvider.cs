namespace AgctorSDK.CodeGraph.Snippets
{
    /// <summary>
    /// Provides language-specific extraction of code snippets (method or class bodies) from a physical source file.
    /// Implementations should return <c>null</c> when they cannot locate the requested element.
    /// </summary>
    public interface ISnippetProvider
    {
        /// <summary>True if the provider can handle <paramref name="filePath"/> (usually by extension).</summary>
        bool CanHandle(string filePath);

        /// <summary>Extract source code for the given <paramref name="methodName"/>.</summary>
        string? GetMethodSource(string filePath, string methodName, int maxLines = 120);

        /// <summary>Extract source code for the given <paramref name="className"/>.</summary>
        string? GetClassSource(string filePath, string className, int maxLines = 400);
    }
} 