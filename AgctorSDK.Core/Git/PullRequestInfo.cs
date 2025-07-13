namespace AgctorSDK.Core.Git
{
    /// <summary>
    /// Data returned after a PullRequestAgent successfully opens a PR.
    /// </summary>
    public sealed class PullRequestInfo
    {
        public required string BranchName { get; init; }
        public required string Url { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
    }
} 