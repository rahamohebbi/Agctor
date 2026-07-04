namespace AgctorSDK.Host.Services;

/// <summary>
/// Persists actor runtime selection for the next Host start (PRD-012 Tier A).
/// </summary>
public interface IUserRuntimeSettingsService
{
    /// <summary>
    /// Merges Agctor runtime keys into appsettings.User.json.
    /// </summary>
    Task PersistAsync(RuntimeSettingsUpdate update, CancellationToken cancellationToken = default);
}

/// <summary>Values written to appsettings.User.json for the next Host boot.</summary>
public sealed class RuntimeSettingsUpdate
{
    public string CanonicalRuntimeId { get; set; } = null!;
    public bool? AllowExperimentalRuntimes { get; set; }
    public string? ProtoHost { get; set; }
    public int? ProtoPort { get; set; }
    public string? OrleansClusterId { get; set; }
    public string? OrleansServiceId { get; set; }
    public string? OrleansGatewayHost { get; set; }
    public int? OrleansGatewayPort { get; set; }
}
