namespace AgctorSDK.Host.Services;

/// <summary>
/// Persists operator overrides for <c>Agctor:LLM</c> into appsettings files (PRD-015).
/// </summary>
public interface ILlmUserSettingsService
{
    /// <summary>Writes <c>Agctor:LLM:DefaultModel</c> to appsettings.json and appsettings.User.json.</summary>
    Task PersistDefaultModelAsync(string model, CancellationToken cancellationToken = default);
}
