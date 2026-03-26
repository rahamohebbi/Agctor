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
    public async Task PersistAsync(string canonicalRuntimeId, string? protoHost, int? protoPort, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRuntimeId);

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

        agctor["DefaultRuntime"] = canonicalRuntimeId;

        if (!string.IsNullOrWhiteSpace(protoHost))
            agctor["ProtoHost"] = protoHost.Trim();
        else
            agctor.Remove("ProtoHost");

        if (protoPort is > 0 and <= 65535)
            agctor["ProtoPort"] = protoPort.Value;
        else
            agctor.Remove("ProtoPort");

        var json = root.ToJsonString(JsonWriteOptions);
        await File.WriteAllTextAsync(UserSettingsPath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated runtime selection: DefaultRuntime={Runtime} in {Path}", canonicalRuntimeId, UserSettingsPath);
    }
}
