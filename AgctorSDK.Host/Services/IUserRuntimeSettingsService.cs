namespace AgctorSDK.Host.Services;

/// <summary>
/// Persists actor runtime selection for the next Host start (PRD-012 Tier A).
/// </summary>
public interface IUserRuntimeSettingsService
{
    /// <summary>
    /// Merges Agctor:DefaultRuntime and optional Proto settings into appsettings.User.json.
    /// </summary>
    Task PersistAsync(string canonicalRuntimeId, string? protoHost, int? protoPort, CancellationToken cancellationToken = default);
}
