using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AgctorSDK.CodeGraph.Analyzers.Roslyn
{
    /// <summary>
    /// Basic C# analyzer that uses Roslyn to extract class and method names.
    /// </summary>
    public sealed class RoslynCodeAnalyzer : ICodeAnalyzer
    {
        public string Language => "csharp";

        public IReadOnlyCollection<string> SupportedFileExtensions { get; } = new[] { ".cs" };

        public Task<ParsedFile> AnalyzeAsync(string filePath, string sourceCode)
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetRoot();

            var parsed = new ParsedFile
            {
                FilePath = filePath,
                Classes = root.DescendantNodes()
                                   .OfType<ClassDeclarationSyntax>()
                                   .Select(ParseClass)
                                   .ToList()
            };
            return Task.FromResult(parsed);
        }

        private static ClassInfo ParseClass(ClassDeclarationSyntax classDecl)
        {
            var classInfo = new ClassInfo
            {
                Name = classDecl.Identifier.ValueText,
                Methods = classDecl.DescendantNodes()
                                      .OfType<MethodDeclarationSyntax>()
                                      .Select(m => new MethodInfo { Name = m.Identifier.ValueText })
                                      .ToList()
            };
            return classInfo;
        }
    }
} 