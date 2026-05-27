using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual;

namespace AgctorSDK.Core.ProjectMemory.Privacy;

/// <summary>File-based privacy operations for the people companion project.</summary>
public sealed class PrivacyMemoryService : IPrivacyMemoryService
{
    private readonly CompanionPrivacySettingsStore _settingsStore = new();
    private readonly IVisualPersonPrivacyPurge? _visualPurge;

    public PrivacyMemoryService(IVisualPersonPrivacyPurge? visualPurge = null)
    {
        _visualPurge = visualPurge;
    }

    public Task<CompanionPrivacySettings> GetSettingsAsync(string projectRoot, CancellationToken cancellationToken = default) =>
        _settingsStore.LoadAsync(projectRoot, cancellationToken);

    public async Task<CompanionPrivacySettings> UpdateSettingsAsync(
        string projectRoot,
        CompanionPrivacySettings settings,
        CancellationToken cancellationToken = default)
    {
        await _settingsStore.SaveAsync(projectRoot, settings, cancellationToken).ConfigureAwait(false);
        return settings;
    }

    public async Task<bool> ForgetPersonAsync(
        string projectRoot,
        string scenarioId,
        string entityKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(entityKey))
            return false;

        var removedPeople = TryDeletePeopleFolder(projectRoot, scenarioId, entityKey);
        var visualRemoved = 0;
        if (_visualPurge != null)
        {
            var purge = await _visualPurge
                .PurgePersonAsync(projectRoot, scenarioId, entityKey, cancellationToken)
                .ConfigureAwait(false);
            visualRemoved = purge.AssetsRemoved;
        }

        return removedPeople || visualRemoved > 0;
    }

    public Task<Stream> ExportScenarioPeopleZipAsync(
        string projectRoot,
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = PersonaScenarioScope.GetEntityWorkspaceRoot(projectRoot, scenarioId);
        var peopleDir = Path.Combine(workspace, "people");
        var visualAssetsDir = Path.Combine(workspace, "visual", "assets");
        var hasPeople = Directory.Exists(peopleDir);
        var hasVisual = Directory.Exists(visualAssetsDir)
                        && Directory.EnumerateFiles(visualAssetsDir, "*.yaml", SearchOption.TopDirectoryOnly).Any();
        if (!hasPeople && !hasVisual)
            throw new InvalidOperationException("Scenario people and visual folders do not exist.");

        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (hasPeople)
            {
                foreach (var file in Directory.EnumerateFiles(peopleDir, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rel = "people/" + Path.GetRelativePath(peopleDir, file).Replace('\\', '/');
                    AddZipEntry(zip, rel, file);
                }
            }

            if (hasVisual)
            {
                foreach (var file in Directory.EnumerateFiles(visualAssetsDir, "*.yaml", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(file);
                    AddZipEntry(zip, "visual/assets/" + name, file);
                }
            }
        }

        ms.Position = 0;
        return Task.FromResult<Stream>(ms);
    }

    private static bool TryDeletePeopleFolder(string projectRoot, string scenarioId, string entityKey)
    {
        var workspace = PersonaScenarioScope.GetEntityWorkspaceRoot(projectRoot, scenarioId);
        var entityDir = Path.Combine(workspace, "people", PersonaScenarioScope.SanitizeFolderSegment(entityKey));
        if (!Directory.Exists(entityDir))
            return false;

        Directory.Delete(entityDir, recursive: true);
        return true;
    }

    private static void AddZipEntry(ZipArchive zip, string entryName, string filePath)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        using var input = File.OpenRead(filePath);
        input.CopyTo(entryStream);
    }
}
