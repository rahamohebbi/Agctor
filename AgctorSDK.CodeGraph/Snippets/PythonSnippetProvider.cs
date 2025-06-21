using System;
using System.IO;
using System.Linq;

namespace AgctorSDK.CodeGraph.Snippets
{
    /// <summary>
    /// Very lightweight snippet extractor for Python that relies on indentation.
    /// Suitable for quick display but not for full semantic analysis.
    /// </summary>
    internal sealed class PythonSnippetProvider : ISnippetProvider
    {
        public bool CanHandle(string filePath) => Path.GetExtension(filePath).Equals(".py", StringComparison.OrdinalIgnoreCase);

        public string? GetMethodSource(string filePath, string methodName, int maxLines = 120)
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);
            int start = Array.FindIndex(lines, l => l.TrimStart().StartsWith($"def {methodName}(", StringComparison.Ordinal));
            if (start == -1) return null;
            var indent = lines[start].TakeWhile(Char.IsWhiteSpace).Count();
            var snippetLines = lines.Skip(start).TakeWhile((l, idx) => idx == 0 || l.Length == 0 || l.TakeWhile(Char.IsWhiteSpace).Count() > indent)
                                       .Take(maxLines).ToList();
            return string.Join("\n", snippetLines);
        }

        public string? GetClassSource(string filePath, string className, int maxLines = 400)
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);
            int start = Array.FindIndex(lines, l =>
                l.TrimStart().StartsWith($"class {className}(", StringComparison.Ordinal) ||
                l.TrimStart().StartsWith($"class {className}:", StringComparison.Ordinal));
            if (start == -1) return null;
            var indent = lines[start].TakeWhile(Char.IsWhiteSpace).Count();
            var snippetLines = lines.Skip(start).TakeWhile((l, idx) => idx == 0 || l.Length == 0 || l.TakeWhile(Char.IsWhiteSpace).Count() > indent)
                                       .Take(maxLines).ToList();
            return string.Join("\n", snippetLines);
        }
    }

    internal static class PythonSnippetProviderRegistration
    {
        static PythonSnippetProviderRegistration()
        {
            SnippetProviderRegistry.Register(new PythonSnippetProvider());
        }
        public static void EnsureRegistered() { }
    }
} 