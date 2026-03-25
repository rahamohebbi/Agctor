using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.Build
{
/// <summary>
/// Locates a solution or project near an edited source file and runs <c>dotnet build</c> so NuGet restore and project references apply (no heuristic skipping of test sources).
/// </summary>
public static class DotNetWorkspaceBuild
{
    /// <summary>Cached: first probe runs <c>dotnet --version</c>; avoids spawning a process on every compile.</summary>
    private static readonly Lazy<bool> DotNetCliCached = new(() =>
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                return false;
            proc.WaitForExit(8_000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    });
    /// <summary>Walks up from <paramref name="sourceFilePath"/> looking for a .sln, then a .csproj.</summary>
    public static string? FindSolutionOrProject(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            return null;
        var full = Path.GetFullPath(sourceFilePath);
        var dir = Path.GetDirectoryName(full);
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var slns = Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly);
            if (slns.Length > 0)
            {
                var demo = slns.FirstOrDefault(p => string.Equals(Path.GetFileName(p), "Demo.sln", StringComparison.OrdinalIgnoreCase));
                return demo ?? slns.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First();
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        dir = Path.GetDirectoryName(Path.GetFullPath(sourceFilePath));
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var projs = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly);
            if (projs.Length == 1)
                return projs[0];
            if (projs.Length > 1)
            {
                // Prefer a test project so transitive Demo build + packages are validated in one step.
                var testFirst = projs.FirstOrDefault(p =>
                    Path.GetFileName(p).Contains("Test", StringComparison.OrdinalIgnoreCase));
                return testFirst ?? projs.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First();
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    /// <summary>Runs <c>dotnet build</c>; restores packages by default.</summary>
    public static async Task<(bool Success, string Output, string Error)> BuildAsync(
        string solutionOrProjectPath,
        CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(solutionOrProjectPath);
        if (!File.Exists(full))
            return (false, string.Empty, $"Build entry not found: {full}");

        var workDir = Path.GetDirectoryName(full) ?? ".";
        var entryName = Path.GetFileName(full);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{entryName}\" --verbosity minimal --nologo",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
                return (false, string.Empty, "Could not start dotnet process.");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            var combined = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(stdout))
                combined.AppendLine(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr))
                combined.AppendLine(stderr.TrimEnd());

            var text = combined.ToString().Trim();
            if (proc.ExitCode == 0)
                return (true, text, string.Empty);

            return (false, text, string.IsNullOrWhiteSpace(text) ? $"dotnet build exited with code {proc.ExitCode}." : text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    /// <summary>True if <c>dotnet</c> is on PATH (cached after first check).</summary>
    public static bool IsDotNetCliAvailable() => DotNetCliCached.Value;
}
}
