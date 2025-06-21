using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AgctorSDK.CodeGraph.Snippets
{
    /// <summary>
    /// Precise snippet extraction for C# using Roslyn syntax trees. Handles both block and expression-bodied members.
    /// </summary>
    internal sealed class CSharpSnippetProvider : ISnippetProvider
    {
        public bool CanHandle(string filePath) => Path.GetExtension(filePath).Equals(".cs", StringComparison.OrdinalIgnoreCase);

        public string? GetMethodSource(string filePath, string methodName, int maxLines = 120)
        {
            if (!File.Exists(filePath)) return null;
            var text = File.ReadAllText(filePath);
            var syntax = CSharpSyntaxTree.ParseText(text);
            var root = syntax.GetRoot();
            // Find the first method with matching identifier (case-insensitive)
            var method = root.DescendantNodes()
                             .OfType<MethodDeclarationSyntax>()
                             .FirstOrDefault(m => string.Equals(m.Identifier.Text, methodName, StringComparison.OrdinalIgnoreCase));
            if (method == null) return null;

            return ExtractSpan(text, method.Span, maxLines);
        }

        public string? GetClassSource(string filePath, string className, int maxLines = 400)
        {
            if (!File.Exists(filePath)) return null;
            var text = File.ReadAllText(filePath);
            var syntax = CSharpSyntaxTree.ParseText(text);
            var root = syntax.GetRoot();
            var cls = root.DescendantNodes()
                          .OfType<ClassDeclarationSyntax>()
                          .FirstOrDefault(c => string.Equals(c.Identifier.Text, className, StringComparison.OrdinalIgnoreCase));
            if (cls == null) return null;

            return ExtractSpan(text, cls.Span, maxLines);
        }

        private static string ExtractSpan(string fullText, TextSpan span, int maxLines)
        {
            // Clamp to requested max lines to avoid giant snippets.
            var text = SourceText.From(fullText);
            var startLine = text.Lines.GetLineFromPosition(span.Start).LineNumber;
            var endLine = text.Lines.GetLineFromPosition(span.End).LineNumber;
            if (endLine - startLine + 1 > maxLines)
            {
                endLine = startLine + maxLines - 1;
            }
            var slice = text.Lines[startLine].Span.Start;
            var sliceEnd = text.Lines[endLine].Span.End;
            return fullText.Substring(slice, sliceEnd - slice);
        }
    }

    /// <summary>
    /// One-time static helper to auto-register the provider via static ctor.
    /// </summary>
    internal static class CSharpSnippetProviderRegistration
    {
        static CSharpSnippetProviderRegistration()
        {
            SnippetProviderRegistry.Register(new CSharpSnippetProvider());
        }

        // Touching this property anywhere forces the static constructor to run.
        public static void EnsureRegistered() { /* intentionally blank */ }
    }
} 