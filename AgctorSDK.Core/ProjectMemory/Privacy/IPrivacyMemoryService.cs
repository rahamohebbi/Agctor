using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Privacy;

/// <summary>PRD-022b: export, forget, and companion privacy settings.</summary>
public interface IPrivacyMemoryService
{
    Task<CompanionPrivacySettings> GetSettingsAsync(string projectRoot, CancellationToken cancellationToken = default);

    Task<CompanionPrivacySettings> UpdateSettingsAsync(
        string projectRoot,
        CompanionPrivacySettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes <c>scenarios/&lt;id&gt;/people/&lt;entityKey&gt;/</c>. Returns false when missing.</summary>
    Task<bool> ForgetPersonAsync(
        string projectRoot,
        string scenarioId,
        string entityKey,
        CancellationToken cancellationToken = default);

    /// <summary>Zip of all files under the scenario people workspace.</summary>
    Task<Stream> ExportScenarioPeopleZipAsync(
        string projectRoot,
        string scenarioId,
        CancellationToken cancellationToken = default);
}
