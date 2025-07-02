using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AgctorSDK.Core.Tools.Implementations.LanguageAdapters
{
    internal sealed class CSharpLanguageAdapter : ILanguageAdapter
    {
        public string Extension => ".cs";

        public string? InsertBySelector(string source, string selector, string snippet)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(source);
                var root = tree.GetRoot();
                var target = ResolveSelector(root, selector);
                if (target == null) return null;
                int insertPos = target.Span.End;
                return source.Insert(insertPos, Environment.NewLine + snippet + Environment.NewLine);
            }
            catch { return null; }
        }

        public string? ReplaceBySelector(string source, string selector, string replacement)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(source);
                var root = tree.GetRoot();
                var target = ResolveSelector(root, selector);
                if (target == null) return null;
                return source.Substring(0, target.Span.Start) + replacement + source.Substring(target.Span.End);
            }
            catch { return null; }
        }

        public async Task<(bool ok, string? formatted)> TryFormatAsync(string source, CancellationToken ct = default)
        {
            var formatter = new AgctorSDK.Core.Tools.Implementations.Format.CSharpFormatter();
            if (!formatter.IsAvailable) return (false, null);
            var (ok, formatted, _) = await formatter.FormatAsync(source);
            return (ok, formatted);
        }

        private Microsoft.CodeAnalysis.SyntaxNode? ResolveSelector(Microsoft.CodeAnalysis.SyntaxNode root, string selector)
        {
            var segments = selector.Split('>');
            Microsoft.CodeAnalysis.SyntaxNode? current = root;
            foreach (var raw in segments)
            {
                var seg = raw.Trim();
                var parts = seg.Split(':');
                if (parts.Length != 2) return null;
                var kind = parts[0].Trim().ToLowerInvariant();
                var name = parts[1].Trim();
                if (kind == "class")
                {
                    current = current.DescendantNodes().OfType<ClassDeclarationSyntax>()
                                     .FirstOrDefault(c => c.Identifier.Text == name);
                }
                else if (kind == "method")
                {
                    current = current.DescendantNodes().OfType<MethodDeclarationSyntax>()
                                     .FirstOrDefault(m => m.Identifier.Text == name);
                }
                else if (kind == "namespace")
                {
                    current = current.DescendantNodes().OfType<NamespaceDeclarationSyntax>()
                                     .FirstOrDefault(n => n.Name.ToString() == name);
                }
                else return null;
                if (current == null) return null;
            }
            return current;
        }
    }
} 