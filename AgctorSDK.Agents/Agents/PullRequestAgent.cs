using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Git;
using Microsoft.Extensions.Logging;

namespace AgctorSDK.Agents.Agents;

/// <summary>
/// Agent that turns the current working directory changes into a git commit, pushes a feature branch, and opens a PR.
/// Expected prompt syntax (v1): "&lt;branch&gt;|&lt;commit&gt;|&lt;pr-title&gt;|&lt;pr-body&gt;". Only branch and commit message are mandatory.
/// </summary>
public sealed class PullRequestAgent : Agent
{
    private readonly IGitService _git;
    private readonly string _repoPath;
    private readonly ILogger<PullRequestAgent> _logger;

    public PullRequestAgent(
        string id,
        IGitService gitService,
        ILogger<PullRequestAgent> logger,
        string? repoPath = null) : base(id)
    {
        _git = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repoPath = repoPath ?? Directory.GetCurrentDirectory();
    }

    protected override async Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken)
    {
        // Parse "branch|commit|title|body"
        var parts = prompt.Split('|', 4, StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            await FinalizeTaskAsFailed(new Exception("Prompt must be '<branch>|<commit>' at minimum"), cancellationToken);
            return;
        }

        var branch = parts[0];
        var commitMsg = parts[1];
        var prTitle = parts.Length > 2 ? parts[2] : commitMsg;
        var prBody = parts.Length > 3 ? parts[3] : null;

        try
        {
            await _git.EnsureRepoAsync(_repoPath, cancellationToken);
            await _git.CheckoutBranchAsync(_repoPath, branch, createIfMissing: true, cancellationToken);
            await _git.StageAllAsync(_repoPath, cancellationToken);
            await _git.CommitAsync(_repoPath, commitMsg, "AgctorBot", "bot@agctor.local", cancellationToken);
            await _git.PushAsync(_repoPath, cancellationToken);

            var pr = await _git.OpenPullRequestAsync(_repoPath, "main", prTitle, prBody, cancellationToken);
            await FinalizeTask(pr, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PullRequestAgent failed");
            await FinalizeTaskAsFailed(ex, cancellationToken);
        }
    }

    protected override bool ShouldDecomposeTask(string prompt) => false;
} 