using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.LanguageCompilers
{
    /// <summary>
    /// Roslyn-based compiler that validates C# in-memory (no on-disk assembly). Single-snippet mode is used for ad-hoc code;
    /// <see cref="CompileSameDirectoryWorkspaceAsync"/> joins all *.cs siblings in the same folder when no SDK project is available (fallback if <c>dotnet</c> is missing or no .sln/.csproj was found).
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

            return await Task.Run(() => CompileInternal(new[] { ("(snippet)", code) })).ConfigureAwait(false);
        }

        /// <summary>
        /// Compiles every *.cs in the same folder as <paramref name="primaryPath"/> (no NuGet; use <c>dotnet build</c> via <see cref="AgctorSDK.Core.Tools.Implementations.CompileTool"/> when a project exists on disk).
        /// </summary>
        public Task<(bool Success, string Output, string Error)> CompileSameDirectoryWorkspaceAsync(string primaryPath)
        {
            if (string.IsNullOrWhiteSpace(primaryPath) || !File.Exists(primaryPath))
                return Task.FromResult((false, string.Empty, "Primary file not found."));

            return Task.Run(() =>
            {
                try
                {
                    var full = Path.GetFullPath(primaryPath);
                    var dir = Path.GetDirectoryName(full);
                    if (string.IsNullOrEmpty(dir))
                        return CompileInternal(new[] { (full, File.ReadAllText(full)) });

                    var sources = new List<(string Path, string Code)>();
                    foreach (var cs in Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly)
                                 .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                        sources.Add((cs, File.ReadAllText(cs)));

                    if (sources.Count == 0)
                        return (false, string.Empty, "No compilable C# sources found in directory.");

                    return CompileInternal(sources);
                }
                catch (Exception ex)
                {
                    return (false, string.Empty, $"Compilation threw exception: {ex.Message}");
                }
            });
        }

        private (bool Success, string Output, string Error) CompileInternal(IReadOnlyList<(string Path, string Code)> sources)
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            try
            {
                var syntaxTrees = sources
                    .Select(s => CSharpSyntaxTree.ParseText(s.Code, path: s.Path))
                    .ToArray();

                var references = BuildMetadataReferences();

                var compilation = CSharpCompilation.Create(
                    assemblyName: $"DynamicAssembly_{Guid.NewGuid():N}",
                    syntaxTrees: syntaxTrees,
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

        private static List<MetadataReference> BuildMetadataReferences()
        {
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location)
            };

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

            TryAddReference("System.Runtime");
            TryAddReference("netstandard");
            TryAddReference("System.Console");
            TryAddReference("System.Collections");

            return references;
        }
    }
}
