namespace AgctorSDK.Host.Services;

/// <summary>
/// Persists <c>Agctor:ProjectMemory:ProjectRoot</c> to <c>appsettings.User.json</c> (PRD-012 pattern).
/// </summary>
public interface IUserProjectMemorySettingsService
{
    Task PersistProjectRootAsync(string absoluteProjectRoot, CancellationToken cancellationToken = default);
}
