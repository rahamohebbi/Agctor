namespace AgctorSDK.Host.Services;

/// <summary>
/// Persists operator overrides for <c>Agctor:LLM</c> into <c>appsettings.User.json</c> (PRD-015).
/// </summary>
public interface ILlmUserSettingsService
{
    /// <summary>Writes <c>Agctor:LLM:DefaultModel</c> while preserving other keys in the user file.</summary>
    Task PersistDefaultModelAsync(string model, CancellationToken cancellationToken = default);
}
