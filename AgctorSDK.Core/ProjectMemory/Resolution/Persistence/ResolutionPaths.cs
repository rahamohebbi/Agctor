using System.IO;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Persistence;

/// <summary>
/// Canonical on-disk paths for the resolution subsystem. Keeping them here avoids scattering
/// magic strings across actors and tests.
/// </summary>
public static class ResolutionPaths
{
    public const string ResolutionFolder = ".resolution";
    public const string IncomingFile = "incoming.yaml";
    public const string PromotionsFile = "promotions.log.yaml";
    public const string PolicyFile = "resolution.yaml";
    public const string AgctorFolder = ".agctor";

    /// <summary>The entity's resolution folder (<c>&lt;entity&gt;/.resolution/</c>).</summary>
    public static string EntityResolutionFolder(string entityRootPath) =>
        Path.Combine(entityRootPath, ResolutionFolder);

    public static string IncomingPath(string entityRootPath) =>
        Path.Combine(EntityResolutionFolder(entityRootPath), IncomingFile);

    public static string PromotionsPath(string entityRootPath) =>
        Path.Combine(EntityResolutionFolder(entityRootPath), PromotionsFile);

    /// <summary>Project-level policy file: <c>&lt;projectRoot&gt;/.agctor/resolution.yaml</c>.</summary>
    public static string PolicyPath(string projectRoot) =>
        Path.Combine(projectRoot, AgctorFolder, PolicyFile);
}
