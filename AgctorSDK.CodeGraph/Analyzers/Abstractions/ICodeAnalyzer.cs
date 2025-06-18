using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgctorSDK.CodeGraph.Analyzers.Abstractions
{
    /// <summary>
    /// Defines a pluggable code analyzer that can parse a source file and return structural information.
    /// </summary>
    public interface ICodeAnalyzer
    {
        /// <summary>
        /// Language identifier (e.g. "csharp", "python").
        /// </summary>
        string Language { get; }

        /// <summary>
        /// File extensions (".cs", ".py", etc.) that this analyzer supports.
        /// Values should include the leading dot and be lowercase.
        /// </summary>
        IReadOnlyCollection<string> SupportedFileExtensions { get; }

        /// <summary>
        /// Parses <paramref name="sourceCode"/> and returns a structured representation of the file.
        /// </summary>
        Task<ParsedFile> AnalyzeAsync(string filePath, string sourceCode);
    }

    /// <summary>
    /// Parsed representation of a source file.
    /// </summary>
    public class ParsedFile
    {
        public string FilePath { get; set; } = string.Empty;
        public List<ClassInfo> Classes { get; set; } = new();
    }

    public class ClassInfo
    {
        public string Name { get; set; } = string.Empty;
        public List<MethodInfo> Methods { get; set; } = new();
    }

    public class MethodInfo
    {
        public string Name { get; set; } = string.Empty;
    }
} 