using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Git
{
    /// <summary>
    /// Minimal abstraction over Git operations needed by PullRequestAgent.
    /// </summary>
    public interface IGitService
    {
        /// <summary>
        /// Ensures the provided directory is a git repository; initializes if not.
        /// </summary>
        Task EnsureRepoAsync(string repoPath, CancellationToken ct = default);

        /// <summary>
        /// Creates (or switches to) the specified branch.
        /// </summary>
        Task CheckoutBranchAsync(string repoPath, string branchName, bool createIfMissing = true, CancellationToken ct = default);

        /// <summary>
        /// Stages all modified files.
        /// </summary>
        Task StageAllAsync(string repoPath, CancellationToken ct = default);

        /// <summary>
        /// Commits staged changes. No-op if nothing to commit.
        /// </summary>
        Task CommitAsync(string repoPath, string message, string authorName, string authorEmail, CancellationToken ct = default);

        /// <summary>
        /// Pushes the current branch to origin.
        /// </summary>
        Task PushAsync(string repoPath, CancellationToken ct = default);

        /// <summary>
        /// Opens a pull request for the current branch targeting the given base branch.
        /// Returns basic PR info (URL etc.).
        /// </summary>
        Task<PullRequestInfo> OpenPullRequestAsync(string repoPath, string baseBranch, string title, string? body = null, CancellationToken ct = default);
    }
} 