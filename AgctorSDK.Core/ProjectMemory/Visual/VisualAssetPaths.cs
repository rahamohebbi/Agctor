using System.IO;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Resolves catalog paths and S3 keys for visual assets.</summary>
public static class VisualAssetPaths
{
    public static string AssetsFolder(string projectRoot, string scenarioId) =>
        Path.Combine(
            PersonaScenarioScope.GetEntityWorkspaceRoot(projectRoot, scenarioId),
            "visual",
            "assets");

    public static string AssetCatalogPath(string projectRoot, string scenarioId, string assetId) =>
        Path.Combine(AssetsFolder(projectRoot, scenarioId), $"{assetId}.yaml");

    public static string BlobKey(string projectId, string scenarioId, string assetId, string extension)
    {
        var ext = extension.TrimStart('.');
        if (string.IsNullOrEmpty(ext))
            ext = "bin";
        var seg = PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
        var pid = PersonaScenarioScope.SanitizeFolderSegment(projectId);
        return $"projects/{pid}/scenarios/{seg}/assets/{assetId}/original.{ext}";
    }

    /// <summary>Stable project id from <c>.agctor/project.yaml</c> or folder name.</summary>
    public static string ResolveProjectId(string projectRoot)
    {
        var root = Path.GetFullPath(projectRoot.Trim());
        var manifestPath = Path.Combine(root, ".agctor", "project.yaml");
        if (File.Exists(manifestPath))
        {
            try
            {
                var manifest = ProjectYamlSerializer.DeserializeFromFile<AgctorProjectManifest>(manifestPath);
                if (!string.IsNullOrWhiteSpace(manifest.ProjectId))
                    return PersonaScenarioScope.SanitizeFolderSegment(manifest.ProjectId);
            }
            catch
            {
                // fall through
            }
        }

        return PersonaScenarioScope.SanitizeFolderSegment(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)));
    }

    public static string ExtensionForMime(string mime)
    {
        return mime.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            "image/heic" => "heic",
            _ => "bin"
        };
    }
}
