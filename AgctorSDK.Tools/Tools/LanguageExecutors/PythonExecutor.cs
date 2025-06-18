using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.LanguageExecutors
{
    /// <summary>
    /// Executor for Python code using IronPython
    /// </summary>
    public class PythonExecutor : ILanguageExecutor
    {
        /// <summary>
        /// Gets the language identifier
        /// </summary>
        public string Language => "python";

        /// <summary>
        /// Executes Python code using IronPython
        /// </summary>
        /// <param name="code">The Python code to execute</param>
        /// <returns>A tuple containing success status, output, and error message if any</returns>
        public async Task<(bool Success, string Output, string Error)> ExecuteCodeAsync(string code)
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            try
            {
                await Task.Run(() =>
                {
                    // Create the Python engine
                    var engine = Python.CreateEngine();
                    var scope = engine.CreateScope();

                    // Redirect stdout and stderr
                    var outputStream = new MemoryStream();
                    var errorStream = new MemoryStream();
                    var outputWriter = new StreamWriter(outputStream);
                    var errorWriter = new StreamWriter(errorStream);

                    engine.Runtime.IO.SetOutput(outputStream, outputWriter);
                    engine.Runtime.IO.SetErrorOutput(errorStream, errorWriter);

                    try
                    {
                        // Execute the code
                        var source = engine.CreateScriptSourceFromString(code, Microsoft.Scripting.SourceCodeKind.Statements);
                        source.Execute(scope);

                        // Flush the writers to ensure all output is captured
                        outputWriter.Flush();
                        errorWriter.Flush();

                        // Get the output and error
                        outputStream.Position = 0;
                        errorStream.Position = 0;

                        using var outputReader = new StreamReader(outputStream, leaveOpen: true);
                        using var errorReader = new StreamReader(errorStream, leaveOpen: true);

                        outputBuilder.Append(outputReader.ReadToEnd());
                        errorBuilder.Append(errorReader.ReadToEnd());
                    }
                    catch (Exception ex)
                    {
                        errorBuilder.Append($"Execution error: {ex.Message}");
                    }
                    finally
                    {
                        // Clean up
                        outputWriter.Dispose();
                        errorWriter.Dispose();
                        outputStream.Dispose();
                        errorStream.Dispose();
                    }
                });

                // If there's an error, return failure
                if (errorBuilder.Length > 0)
                {
                    return (false, outputBuilder.ToString(), errorBuilder.ToString());
                }

                return (true, outputBuilder.ToString(), string.Empty);
            }
            catch (Exception ex)
            {
                return (false, outputBuilder.ToString(), $"Error setting up Python execution: {ex.Message}");
            }
        }
    }
} 