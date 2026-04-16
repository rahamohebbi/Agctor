using System.Diagnostics;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Lists <c>git status --porcelain</c> entries that fall under the active portable project root (e.g. <c>samples/people-project</c> when nested in a larger repo).
/// </summary>
public static class ProjectMemoryGitWorkspaceScanner
{
    /// <summary>Walk parents until a <c>.git</c> directory exists.</summary>
    public static string? FindGitRoot(string startDirectory)
    {
        var d = Path.GetFullPath(startDirectory.Trim());
        for (var i = 0; i < 128; i++)
        {
            if (Directory.Exists(Path.Combine(d, ".git")))
                return d;
            var p = Directory.GetParent(d);
            if (p == null)
                break;
            d = p.FullName;
        }

        return null;
    }

    /// <summary>
    /// Parses one <c>git status --porcelain</c> line (with <c>core.quotepath=false</c>) into status + path relative to the Git repo root.
    /// </summary>
    public static bool TryParsePorcelainLine(string line, out string status, out string pathFromGitRoot)
    {
        status = "";
        pathFromGitRoot = "";
        if (string.IsNullOrWhiteSpace(line))
            return false;
        // Renames: "XY old/path -> new/path"
        var trimmed = line.TrimEnd('\r');
        if (trimmed.Length < 3)
            return false;
        status = trimmed[..2];
        var i = 2;
        while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
            i++;
        if (i >= trimmed.Length)
            return false;
        var pathPart = trimmed[i..].Trim();
        if (pathPart.Contains(" -> ", StringComparison.Ordinal))
        {
            var parts = pathPart.Split(" -> ", 2, StringSplitOptions.TrimEntries);
            pathPart = parts[^1];
        }

        pathFromGitRoot = pathPart.Replace('\\', '/').TrimStart('/');
        return pathPart.Length > 0;
    }

    public static async Task<WorkspaceGitChangesDto> ListChangesUnderProjectRootAsync(
        string projectRoot,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var pr = Path.GetFullPath(projectRoot.Trim());
        if (!Directory.Exists(pr))
        {
            return new WorkspaceGitChangesDto
            {
                GitAvailable = false,
                Message = "Project root is not a directory.",
                Files = Array.Empty<WorkspaceGitChangeItemDto>()
            };
        }

        var gitRoot = FindGitRoot(pr);
        if (gitRoot == null)
        {
            return new WorkspaceGitChangesDto
            {
                GitAvailable = false,
                Message = "No Git repository (.git) found above the project root.",
                Files = Array.Empty<WorkspaceGitChangeItemDto>()
            };
        }

        string stdout;
        string stderr;
        int exit;
        try
        {
            (stdout, stderr, exit) = await RunGitAsync(gitRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Git workspace scan: failed to run git");
            return new WorkspaceGitChangesDto
            {
                GitAvailable = false,
                GitRoot = gitRoot,
                Message = "Git is not available on PATH or failed to run.",
                Files = Array.Empty<WorkspaceGitChangeItemDto>()
            };
        }

        if (exit != 0)
        {
            return new WorkspaceGitChangesDto
            {
                GitAvailable = false,
                GitRoot = gitRoot,
                Message = string.IsNullOrWhiteSpace(stderr) ? $"git exited with code {exit}." : stderr.Trim(),
                Files = Array.Empty<WorkspaceGitChangeItemDto>()
            };
        }

        var gitRootFull = Path.GetFullPath(gitRoot);
        var prFull = Path.GetFullPath(pr.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var list = new List<WorkspaceGitChangeItemDto>();
        foreach (var rawLine in stdout.Split('\n', StringSplitOptions.None))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (!TryParsePorcelainLine(line, out var st, out var relFromRepo))
                continue;

            var abs = Path.GetFullPath(Path.Combine(gitRootFull, relFromRepo.Replace('/', Path.DirectorySeparatorChar)));
            if (!PersonaScenarioScope.IsUnderProjectRoot(prFull, abs))
                continue;

            var relProject = Path.GetRelativePath(prFull, abs).Replace('\\', '/');
            list.Add(new WorkspaceGitChangeItemDto { Status = st, RelativePath = relProject });
        }

        list.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        return new WorkspaceGitChangesDto
        {
            GitAvailable = true,
            GitRoot = gitRootFull,
            Message = null,
            Files = list
        };
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunGitAsync(string gitRoot, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(gitRoot);
        psi.ArgumentList.Add("status");
        psi.ArgumentList.Add("--porcelain");

        using var p = new Process { StartInfo = psi };
        if (!p.Start())
            throw new InvalidOperationException("Failed to start git.");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (stdout, stderr, p.ExitCode);
    }
}
