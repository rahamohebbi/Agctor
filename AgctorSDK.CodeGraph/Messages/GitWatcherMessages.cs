namespace AgctorSDK.CodeGraph.Messages
{
    public record CreateSnapshotMessage(string CommitSha);
    public record SnapshotCreatedMessage(string CommitSha, string Path);
} 