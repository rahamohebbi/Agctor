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

                // If selector targets a class, handle smart insertion + duplicate checks
                if (target is ClassDeclarationSyntax cls)
                {
                    var className = cls.Identifier.Text;

                    // Gather existing method names in the class for duplicate detection
                    var existingMethodNames = cls.Members
                        .OfType<MethodDeclarationSyntax>()
                        .Select(m => m.Identifier.Text)
                        .ToHashSet(StringComparer.Ordinal);

                    // Parse the snippet to discover method declarations inside it
                    var dummyWrap = $"class Dummy {{ {snippet} }}";
                    var snippetTree = CSharpSyntaxTree.ParseText(dummyWrap);
                    var snippetRoot = snippetTree.GetRoot();
                    var snippetMethods = snippetRoot.DescendantNodes()
                        .OfType<MethodDeclarationSyntax>()
                        .ToList();

                    // Remove duplicates by replacing existing methods instead of inserting new ones
                    string updatedSource = source;
                    foreach (var sm in snippetMethods)
                    {
                        var name = sm.Identifier.Text;
                        var smText = sm.ToFullString();

                        if (existingMethodNames.Contains(name))
                        {
                            // Use ReplaceBySelector to update existing method implementation
                            var selectorForReplace = $"class:{className} > method:{name}";
                            updatedSource = ReplaceBySelector(updatedSource, selectorForReplace, smText) ?? updatedSource;
                        }
                    }

                    // For non-duplicates, build the content to insert
                    var newMembers = snippetMethods
                        .Where(sm => !existingMethodNames.Contains(sm.Identifier.Text))
                        .Select(sm => sm.ToFullString())
                        .ToList();

                    if (newMembers.Count == 0)
                        return updatedSource; // nothing new to insert

                    // Determine indentation based on first existing member if present
                    var indentTrivia = "    "; // 4 spaces default
                    if (cls.Members.FirstOrDefault() is MethodDeclarationSyntax firstMethod)
                    {
                        var leading = firstMethod.GetLeadingTrivia().ToString();
                        var lastNl = leading.LastIndexOf('\n');
                        if (lastNl >= 0 && lastNl < leading.Length - 1)
                        {
                            var indentCandidate = leading.Substring(lastNl + 1);
                            if (!string.IsNullOrWhiteSpace(indentCandidate)) indentTrivia = indentCandidate;
                        }
                    }

                    var insertPos = cls.CloseBraceToken.FullSpan.Start;
                    var contentToInsert = System.Environment.NewLine + string.Join(System.Environment.NewLine, newMembers.Select(m => indentTrivia + m.Trim())) + System.Environment.NewLine;

                    updatedSource = updatedSource.Insert(insertPos, contentToInsert);
                    return updatedSource;
                }
                else
                {
                    // Fallback: insert after target node
                    int insertPos = target.Span.End;
                    var contentToInsert = System.Environment.NewLine + snippet + System.Environment.NewLine;
                    return source.Insert(insertPos, contentToInsert);
                }
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