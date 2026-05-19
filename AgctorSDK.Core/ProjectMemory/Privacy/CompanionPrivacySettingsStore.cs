using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Privacy;

/// <summary>Reads/writes companion privacy settings beside other runtime YAML.</summary>
public sealed class CompanionPrivacySettingsStore
{
    public static string SettingsPath(string projectRoot) =>
        Path.Combine(Path.GetFullPath(projectRoot.Trim()), ".agctor", "runtime", "companion-privacy.yaml");

    public Task<CompanionPrivacySettings> LoadAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = SettingsPath(projectRoot);
        if (!File.Exists(path))
            return Task.FromResult(new CompanionPrivacySettings());

        try
        {
            var text = File.ReadAllText(path);
            var settings = string.IsNullOrWhiteSpace(text)
                ? new CompanionPrivacySettings()
                : ProjectYamlSerializer.Deserialize<CompanionPrivacySettings>(text) ?? new CompanionPrivacySettings();
            return Task.FromResult(settings);
        }
        catch
        {
            return Task.FromResult(new CompanionPrivacySettings());
        }
    }

    public Task SaveAsync(string projectRoot, CompanionPrivacySettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = SettingsPath(projectRoot);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, ProjectYamlSerializer.Serialize(settings ?? new CompanionPrivacySettings()));
        return Task.CompletedTask;
    }
}
