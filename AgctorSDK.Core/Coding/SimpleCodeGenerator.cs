using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Tasks;

namespace AgctorSDK.Core.Coding;

/// <summary>
/// Extremely lightweight code generator that turns a <see cref="ProjectTask"/> into one source file under the project's ./Generated folder.
/// Intended as a placeholder until a smarter LLM-driven generator is hooked in.
/// </summary>
public sealed class SimpleCodeGenerator : ICodeGenerator
{
    private readonly string _outputRoot;

    /// <param name="outputRoot">Folder where generated files should be written. Defaults to ./Generated relative to the current working directory.</param>
    public SimpleCodeGenerator(string? outputRoot = null)
    {
        _outputRoot = outputRoot ?? Path.Combine(Environment.CurrentDirectory, "Generated");
    }

    public async Task<CodeGenerationResult> GenerateAsync(ProjectTask task, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_outputRoot);
            var safeName = string.Join("_", task.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var fileName = safeName + ".txt";
            var path = Path.Combine(_outputRoot, fileName);

            var content = $"# Auto-generated stub for task: {task.Title}\n\n" +
                          $"GoalId: {task.GoalId}\nTaskId: {task.Id}\n\n" +
                          $"Description:\n{task.Description}\n";

            // Write asynchronously – overwrite if exists
            await File.WriteAllTextAsync(path, content, cancellationToken);

            return new CodeGenerationResult
            {
                Success = true,
                Summary = $"Created file {path}",
                Patches = new[] { (path, content) }
            };
        }
        catch (Exception ex)
        {
            return new CodeGenerationResult
            {
                Success = false,
                Error = ex.Message,
                Patches = Array.Empty<(string path, string content)>()
            };
        }
    }
} 