using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers.Abstractions;

namespace AgctorSDK.CodeGraph.Analyzers.Stubs
{
    /// <summary>
    /// Placeholder analyzer that mimics analysing Python/Rust files until proper implementation is provided.
    /// </summary>
    public sealed class TreeSitterAnalyzer : ICodeAnalyzer
    {
        public string Language => "python"; // Treat stub as Python analyzer

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new[] { ".py" };

        public Task<ParsedFile> AnalyzeAsync(string filePath, string sourceCode)
        {
            // Return dummy data but in correct structure.
            var parsed = new ParsedFile
            {
                FilePath = filePath,
                Classes = new List<ClassInfo>
                {
                    new()
                    {
                        Name = "StubClass",
                        Methods = new List<MethodInfo>
                        {
                            new() { Name = "stub_method" }
                        }
                    }
                }
            };
            return Task.FromResult(parsed);
        }
    }
} 