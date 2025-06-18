using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.LanguageExecutors
{
    /// <summary>
    /// Executor for C# code using Roslyn compiler
    /// </summary>
    public class CSharpExecutor : ILanguageExecutor
    {
        /// <summary>
        /// Gets the language identifier
        /// </summary>
        public string Language => "csharp";

        /// <summary>
        /// Compiles and executes C# code
        /// </summary>
        /// <param name="code">The C# code to execute</param>
        /// <returns>A tuple containing success status, output, and error message if any</returns>
        public async Task<(bool Success, string Output, string Error)> ExecuteCodeAsync(string code)
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            try
            {
                // Create compilation
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                
                // Add all necessary references for a basic console application
                var references = new List<MetadataReference>
                {
                    // Basic references
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                };
                
                // Additional references for .NET Core/.NET 5+
                try { references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Console").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Text").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.IO").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Threading").Location)); } catch { }
                try { references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location)); } catch { }

                var compilation = CSharpCompilation.Create(
                    $"DynamicAssembly_{Guid.NewGuid():N}",
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                );

                // Compile in memory
                using var ms = new MemoryStream();
                var result = compilation.Emit(ms);

                if (!result.Success)
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        errorBuilder.AppendLine(diagnostic.ToString());
                    }
                    return (false, string.Empty, errorBuilder.ToString());
                }

                // Prepare to execute
                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());
                
                // Redirect stdout
                var originalOut = Console.Out;
                using var sw = new StringWriter();
                Console.SetOut(sw);

                try
                {
                    // Find entry point and invoke
                    var entryPoint = assembly.EntryPoint;
                    if (entryPoint == null)
                    {
                        return (false, string.Empty, "No entry point found in the code.");
                    }

                    await Task.Run(() => {
                        try
                        {
                            // Check if the method expects parameters
                            var parameters = entryPoint.GetParameters();
                            if (parameters.Length == 0)
                            {
                                // No parameters
                                entryPoint.Invoke(null, null);
                            }
                            else
                            {
                                // Expects string[] args
                                entryPoint.Invoke(null, new object[] { new string[0] });
                            }
                        }
                        catch (Exception ex)
                        {
                            errorBuilder.AppendLine($"Execution error: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                errorBuilder.AppendLine($"Inner exception: {ex.InnerException.Message}");
                            }
                        }
                    });
                    
                    outputBuilder.Append(sw.ToString());
                }
                finally
                {
                    // Restore stdout
                    Console.SetOut(originalOut);
                }

                if (errorBuilder.Length > 0)
                {
                    return (false, outputBuilder.ToString(), errorBuilder.ToString());
                }

                return (true, outputBuilder.ToString(), string.Empty);
            }
            catch (Exception ex)
            {
                return (false, outputBuilder.ToString(), $"Execution error: {ex.Message}");
            }
        }
    }
} 