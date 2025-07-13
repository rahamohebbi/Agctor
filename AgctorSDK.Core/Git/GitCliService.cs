using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Utils;

namespace AgctorSDK.Core.Git;

/// <summary>
/// Shell-based implementation of <see cref="IGitService"/> built on top of <see cref="GitCliHelper"/> utilities.
/// Designed for local repositories and CI agents with Git + GitHub CLI (gh) installed.
/// In production <see cref="OpenPullRequestAsync"/> should be swapped for a GitHub/GitLab API client.
/// </summary>
public sealed class GitCliService : IGitService
{
    private static async Task RunAsync(string repo, string args, CancellationToken ct)
    {
        var (outp, err, code) = await ExecuteAsync("git", args, repo, ct);
        if (code != 0)
            throw new Exception($"git {args} failed: {err} {outp}");
    }

    private static async Task<(string stdout, string stderr, int code)> ExecuteAsync(string exe, string args, string cwd, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync(ct);
        return (await outTask, await errTask, proc.ExitCode);
    }

    public async Task EnsureRepoAsync(string repoPath, CancellationToken ct = default)
    {
        if (!await GitCliHelper.IsGitRepositoryAsync(repoPath))
            await GitCliHelper.InitAsync(repoPath);
    }

    public async Task CheckoutBranchAsync(string repoPath, string branchName, bool createIfMissing = true, CancellationToken ct = default)
    {
        var listArgs = $"branch --list {branchName}";
        var (outp, _, _) = await ExecuteAsync("git", listArgs, repoPath, ct);
        var exists = !string.IsNullOrWhiteSpace(outp);
        var arg = createIfMissing && !exists ? $"checkout -b {branchName}" : $"checkout {branchName}";
        await RunAsync(repoPath, arg, ct);
    }

    public Task StageAllAsync(string repoPath, CancellationToken ct = default) => RunAsync(repoPath, "add .", ct);

    public Task CommitAsync(string repoPath, string message, string authorName, string authorEmail, CancellationToken ct = default)
        => GitCliHelper.CommitAsync(repoPath, message, authorName, authorEmail);

    public Task PushAsync(string repoPath, CancellationToken ct = default) => RunAsync(repoPath, "push -u origin HEAD", ct);

    public async Task<PullRequestInfo> OpenPullRequestAsync(string repoPath, string baseBranch, string title, string? body = null, CancellationToken ct = default)
    {
        // First attempt using GitHub CLI (gh). If unavailable, return dummy URL.
        try
        {
            var args = $"pr create --base {baseBranch} --title \"{title}\"" + (body != null ? $" --body \"{body.Replace("\"", "\\\"")}\"" : string.Empty) + " --head HEAD --fill --yes";
            var (outp, err, code) = await ExecuteAsync("gh", args, repoPath, ct);
            if (code != 0) throw new Exception(err);

            // gh outputs created URL last line
            var url = outp.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last();
            var branch = await GitCliHelper.GetLatestCommitHashAsync(repoPath) ?? "head";
            return new PullRequestInfo { BranchName = branch, Url = url, Title = title, Description = body };
        }
        catch (Exception ex)
        {
            var dummyUrl = $"pr://local/{Guid.NewGuid()}";
            return new PullRequestInfo { BranchName = "feature", Url = dummyUrl, Title = title, Description = $"(mock) {ex.Message}" };
        }
    }
} 