using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.LanguageCompilers
{
    /// <summary>
    /// Roslyn-based compiler that builds a single C# source unit in-memory. It does *not* emit an executable on disk; instead it returns diagnostics so callers can validate compilation.
    /// </summary>
    public class CSharpCompiler : ILanguageCompiler
    {
        public string Language => "csharp";

        public async Task<(bool Success, string Output, string Error)> CompileCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return (false, string.Empty, "Source code is empty.");
            }

            return await Task.Run(() => CompileInternal(code));
        }

        private (bool Success, string Output, string Error) CompileInternal(string code)
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            try
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);

                var references = new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location)
                };

                // Add common core assemblies where available
                TryAddReference("System.Runtime");
                TryAddReference("netstandard");
                TryAddReference("System.Console");
                TryAddReference("System.Collections");

                void TryAddReference(string assemblyName)
                {
                    try
                    {
                        var asm = Assembly.Load(assemblyName);
                        references.Add(MetadataReference.CreateFromFile(asm.Location));
                    }
                    catch
                    {
                        // Ignore – assembly not found (e.g., different BCL layout)
                    }
                }

                var compilation = CSharpCompilation.Create(
                    assemblyName: $"DynamicAssembly_{Guid.NewGuid():N}",
                    syntaxTrees: new[] { syntaxTree },
                    references: references,
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                using var ms = new MemoryStream();
                var result = compilation.Emit(ms);

                foreach (var diagnostic in result.Diagnostics)
                {
                    var line = diagnostic.ToString();
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    {
                        errorBuilder.AppendLine(line);
                    }
                    else
                    {
                        outputBuilder.AppendLine(line);
                    }
                }

                return (result.Success, outputBuilder.ToString(), errorBuilder.ToString());
            }
            catch (Exception ex)
            {
                return (false, outputBuilder.ToString(), $"Compilation threw exception: {ex.Message}");
            }
        }
    }
} 