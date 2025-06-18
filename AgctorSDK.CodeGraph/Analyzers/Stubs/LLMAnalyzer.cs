using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers.Abstractions;

namespace AgctorSDK.CodeGraph.Analyzers.Stubs
{
    /// <summary>
    /// Dummy analyzer that stands in for LLM-based analysis of unsupported languages.
    /// Always returns a single class "LLMFallback" with one method "DoThing".
    /// </summary>
    public sealed class LLMAnalyzer : ICodeAnalyzer
    {
        public string Language => "llm-fallback";

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new[] { ".txt" };

        public Task<ParsedFile> AnalyzeAsync(string filePath, string sourceCode)
        {
            var parsed = new ParsedFile
            {
                FilePath = filePath,
                Classes = new List<ClassInfo>
                {
                    new()
                    {
                        Name = "LLMFallback",
                        Methods = new List<MethodInfo>
                        {
                            new() { Name = "DoThing" }
                        }
                    }
                }
            };
            return Task.FromResult(parsed);
        }
    }
} 