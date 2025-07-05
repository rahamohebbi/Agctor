using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic; // Added for List
using System.Text.RegularExpressions; // Added for Regex

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
                if (target == null) 
                {
                    return null;
                }

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
                    {
                        return updatedSource; // nothing new to insert
                    }

                    // Determine indentation based on first existing member if present
                    var indentTrivia = "    "; // default

                    if (cls.Members.FirstOrDefault() is MethodDeclarationSyntax firstMethod)
                    {
                        var fullText = source;
                        var spanStart = firstMethod.Span.Start;

                        // Walk backwards from method start to find the indentation on its line
                        int lineStart = fullText.LastIndexOf('\n', spanStart);
                        
                        if (lineStart >= 0)
                        {
                            int indentStart = lineStart + 1;
                            int indentEnd = indentStart;
                            while (indentEnd < fullText.Length && (fullText[indentEnd] == ' ' || fullText[indentEnd] == '\t'))
                                indentEnd++;
                            indentTrivia = fullText.Substring(indentStart, indentEnd - indentStart);
                        }
                    }

                    // Apply indentTrivia to every line of each new member
                    List<string> indentedMembers = new();
                    foreach (var memRaw in newMembers)
                    {
                        var mem = ExpandSingleLineMethod(memRaw);
                        var lines = mem.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        
                        for (int i = 0; i < lines.Length; i++)
                        {
                            var line = lines[i];
                            
                            // FIXED: Preserve relative indentation instead of trimming all whitespace
                            if (string.IsNullOrWhiteSpace(line))
                            {
                                lines[i] = "";
                            }
                            else
                            {
                                // Add class-level indentation while preserving existing relative indentation
                                lines[i] = indentTrivia + line;
                            }
                        }
                        
                        var indentedMember = string.Join(Environment.NewLine, lines);
                        indentedMembers.Add(indentedMember);
                    }

                    var insertPos = cls.CloseBraceToken.FullSpan.Start;
                    var fullBlock = string.Join(Environment.NewLine, indentedMembers);
                    var contentToInsert = Environment.NewLine + fullBlock + Environment.NewLine;

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
            catch (Exception ex) 
            { 
                Console.WriteLine($"[CSharpLanguageAdapter] Exception in InsertBySelector: {ex.Message}");
                Console.WriteLine($"[CSharpLanguageAdapter] Stack trace: {ex.StackTrace}");
                return null; 
            }
        }

        public string? ReplaceBySelector(string source, string selector, string replacement)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(source);
                var root = tree.GetRoot();
                var target = ResolveSelector(root, selector);
                if (target == null) return null;

                if (string.IsNullOrEmpty(replacement))
                {
                    // Remove leading indentation and any blank line
                    int start = target.Span.Start;
                    // Walk backwards to first non-white char or newline
                    while (start > 0 && char.IsWhiteSpace(source[start-1]) && source[start-1] != '\n' && source[start-1] != '\r')
                        start--;
                    // If we are at indentation after newline, include the newline too
                    if (start > 0 && (source[start-1] == '\n' || source[start-1] == '\r'))
                    {
                        start--;
                        if (start>0 && source[start-1]=='\r' && source[start]=='\n') start--; // handle CRLF
                    }
                    int end = target.Span.End;
                    // Also remove following whitespace-only line
                    int idx = end;
                    while (idx < source.Length && (source[idx]==' ' || source[idx]=='\t')) idx++;
                    if (idx < source.Length && (source[idx]=='\r' || source[idx]=='\n'))
                    {
                        idx++;
                        if (idx < source.Length && source[idx]=='\n' && source[idx-1]=='\r') idx++;
                        end = idx;
                    }
                    return source.Substring(0, start) + source.Substring(end);
                }

                return source.Substring(0, target.Span.Start) + replacement + source.Substring(target.Span.End);
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[CSharpLanguageAdapter] Exception in ReplaceBySelector: {ex.Message}");
                Console.WriteLine($"[CSharpLanguageAdapter] Stack trace: {ex.StackTrace}");
                return null; 
            }
        }

        public async Task<(bool ok, string? formatted)> TryFormatAsync(string source, CancellationToken ct = default)
        {
            var formatter = new AgctorSDK.Core.Tools.Implementations.Format.CSharpFormatter();
            if (!formatter.IsAvailable) return (false, null);
            var (ok, formatted, _) = await formatter.FormatAsync(source);
            return (ok, formatted);
        }

        private static string ExpandSingleLineMethod(string snippet)
        {
            // Detect pattern: signature { body }
            var m = Regex.Match(snippet.Trim(), @"^(.*?\))\s*\{\s*(.*?;)\s*\}$");
            if (!m.Success) 
            {
                return snippet;
            }
            
            var sign = m.Groups[1].Value.Trim();
            var body = m.Groups[2].Value.Trim();
            
            if (body.EndsWith(";")) body = body.Substring(0, body.Length - 1).TrimEnd();
            var ind = "    "; // 4 spaces inner
            var nl = Environment.NewLine;
            
            return sign + nl + "{" + nl + ind + body + ";" + nl + "}";
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