using System.Collections.Generic;
using AgctorSDK.CodeGraph.Snapshots;

namespace AgctorSDK.CodeGraph.Messages
{
    public record ReviewCommitMessage(string CommitSha, SnapshotDiffResult Diff);

    public record FileComment(string FilePath, int Line, string Suggestion);
    public record CodeReviewResult(string Summary, IReadOnlyCollection<FileComment> Comments, int Score);
} 