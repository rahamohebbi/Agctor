using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers.Abstractions;
using AgctorSDK.CodeGraph.Llm;

namespace AgctorSDK.CodeGraph.Analyzers.Stubs
{
    /// <summary>
    /// LLM-based fallback analyzer used when no static analyzer is available.
    /// Uses an <see cref="ILlmClient"/> to ask the model to emit JSON with structure.
    /// </summary>
    public sealed class LLMAnalyzer : ICodeAnalyzer
    {
        private readonly ILlmClient _llm;

        public LLMAnalyzer(ILlmClient llmClient)
        {
            _llm = llmClient;
        }

        public string Language => "llm-fallback";

        // Wildcard – we act as universal fallback.
        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new[] { "*" };

        public async Task<ParsedFile> AnalyzeAsync(string filePath, string sourceCode)
        {
            var prompt = BuildPrompt(sourceCode);
            var completion = await _llm.CompleteAsync(prompt);

            try
            {
                var dto = JsonSerializer.Deserialize<ParsedFile>(completion, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (dto != null)
                {
                    dto.FilePath = filePath;
                    return dto;
                }
            }
            catch
            {
                // fall through – if parsing fails we'll return trivial result
            }

            // Fallback dummy structure
            return new ParsedFile
            {
                FilePath = filePath,
                Classes = new List<ClassInfo>
                {
                    new() { Name = "Unknown", Methods = new List<MethodInfo>() }
                }
            };
        }

        private static string BuildPrompt(string source)
        {
            return $$"""
You are a static code analysis assistant. Given the following source code, output ONLY a JSON object that matches this C# schema (no extra keys):

{
  "classes": [
    {
      "name": "string",          // class or top-level type name
      "methods": [ { "name": "string" } ]
    }
  ]
}

Source Code:
```text
{source}
```
""";
        }
    }
} 