using System.IO;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

public static class GenericInboxPaths
{
    public static string InboxDirectory(string projectRoot) =>
        Path.GetFullPath(Path.Combine(projectRoot.Trim(), ".agctor", "runtime", "generic-inbox"));

    public static string PendingFile(string projectRoot) => Path.Combine(InboxDirectory(projectRoot), "pending.yaml");

    public static string ConfirmedFile(string projectRoot) => Path.Combine(InboxDirectory(projectRoot), "confirmed.yaml");
}
