using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Writes runtime selection to appsettings.User.json (same pattern as AgentTypeEnablementService).
/// </summary>
public sealed class UserRuntimeSettingsService : IUserRuntimeSettingsService
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<UserRuntimeSettingsService> _logger;

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public UserRuntimeSettingsService(IHostEnvironment environment, ILogger<UserRuntimeSettingsService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    private string UserSettingsPath => Path.Combine(_environment.ContentRootPath, "appsettings.User.json");

    /// <inheritdoc />
    public async Task PersistAsync(RuntimeSettingsUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.CanonicalRuntimeId);

        JsonObject root;
        if (File.Exists(UserSettingsPath))
        {
            var text = await File.ReadAllTextAsync(UserSettingsPath, cancellationToken).ConfigureAwait(false);
            root = JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        var agctor = root["Agctor"]?.AsObject() ?? new JsonObject();
        root["Agctor"] = agctor;

        // Machine-local file only — not appsettings.json (actor backend varies per host).
        agctor["DefaultRuntime"] = update.CanonicalRuntimeId;

        if (update.AllowExperimentalRuntimes.HasValue)
            agctor["AllowExperimentalRuntimes"] = update.AllowExperimentalRuntimes.Value;

        // Only touch keys that were supplied so partial saves keep other Agctor settings.
        SetIfPresent(agctor, "ProtoHost", update.ProtoHost);
        SetIfPresentInt(agctor, "ProtoPort", update.ProtoPort);
        SetIfPresent(agctor, "OrleansClusterId", update.OrleansClusterId);
        SetIfPresent(agctor, "OrleansServiceId", update.OrleansServiceId);
        SetIfPresent(agctor, "OrleansGatewayHost", update.OrleansGatewayHost);
        SetIfPresentInt(agctor, "OrleansGatewayPort", update.OrleansGatewayPort);

        var json = root.ToJsonString(JsonWriteOptions);
        await File.WriteAllTextAsync(UserSettingsPath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated runtime selection: DefaultRuntime={Runtime} in {Path}", update.CanonicalRuntimeId, UserSettingsPath);
    }

    private static void SetIfPresent(JsonObject parent, string key, string? value)
    {
        if (value == null) return;
        if (string.IsNullOrWhiteSpace(value))
            parent.Remove(key);
        else
            parent[key] = value.Trim();
    }

    private static void SetIfPresentInt(JsonObject parent, string key, int? value)
    {
        if (value == null) return;
        if (value is > 0 and <= 65535)
            parent[key] = value.Value;
        else
            parent.Remove(key);
    }
}
