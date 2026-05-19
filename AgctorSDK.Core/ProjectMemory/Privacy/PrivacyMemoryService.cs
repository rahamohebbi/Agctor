using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Privacy;

/// <summary>File-based privacy operations for the people companion project.</summary>
public sealed class PrivacyMemoryService : IPrivacyMemoryService
{
    private readonly CompanionPrivacySettingsStore _settingsStore = new();

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

    public Task<bool> ForgetPersonAsync(
        string projectRoot,
        string scenarioId,
        string entityKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(entityKey))
            return Task.FromResult(false);

        var workspace = PersonaScenarioScope.GetEntityWorkspaceRoot(projectRoot, scenarioId);
        var entityDir = Path.Combine(workspace, "people", PersonaScenarioScope.SanitizeFolderSegment(entityKey));
        if (!Directory.Exists(entityDir))
            return Task.FromResult(false);

        Directory.Delete(entityDir, recursive: true);
        return Task.FromResult(true);
    }

    public Task<Stream> ExportScenarioPeopleZipAsync(
        string projectRoot,
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = PersonaScenarioScope.GetEntityWorkspaceRoot(projectRoot, scenarioId);
        var peopleDir = Path.Combine(workspace, "people");
        if (!Directory.Exists(peopleDir))
            throw new InvalidOperationException("Scenario people folder does not exist.");

        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(peopleDir, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(peopleDir, file).Replace('\\', '/');
                var entry = zip.CreateEntry(rel, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var input = File.OpenRead(file);
                input.CopyTo(entryStream);
            }
        }

        ms.Position = 0;
        return Task.FromResult<Stream>(ms);
    }
}
