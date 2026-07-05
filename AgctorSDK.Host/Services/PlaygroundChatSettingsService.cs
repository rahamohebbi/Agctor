using System.Text.Json;
using System.Text.Json.Nodes;
using AgctorSDK.Core.Sessions;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.Services;

/// <inheritdoc />
public sealed class PlaygroundChatSettingsService : IPlaygroundChatSettingsService
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PlaygroundChatSettingsService> _logger;

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public PlaygroundChatSettingsService(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<PlaygroundChatSettingsService> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    private string UserSettingsPath => Path.Combine(_environment.ContentRootPath, "appsettings.User.json");

    /// <inheritdoc />
    public int GetMaxConversationTurns() =>
        PlaygroundChatSettings.Resolve(_configuration.GetValue<int?>(PlaygroundChatSettings.ConfigKey));

    /// <inheritdoc />
    public PlaygroundChatSettingsDto GetSettings() => new()
    {
        MaxConversationTurns = GetMaxConversationTurns(),
        MinMaxConversationTurns = PlaygroundChatSettings.MinMaxConversationTurns,
        MaxMaxConversationTurns = PlaygroundChatSettings.MaxMaxConversationTurns
    };

    /// <inheritdoc />
    public async Task<PlaygroundChatSettingsDto> SaveAsync(
        PlaygroundChatSettingsUpdateDto update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var clamped = PlaygroundChatSettings.Clamp(update.MaxConversationTurns);

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

        var pm = agctor["ProjectMemory"]?.AsObject() ?? new JsonObject();
        agctor["ProjectMemory"] = pm;
        pm["MaxConversationTurns"] = clamped;

        await File.WriteAllTextAsync(UserSettingsPath, root.ToJsonString(JsonWriteOptions), cancellationToken)
            .ConfigureAwait(false);

        if (_configuration is IConfigurationRoot configRoot)
            configRoot.Reload();

        _logger.LogInformation(
            "Updated {Key}={Value} in {Path}",
            PlaygroundChatSettings.ConfigKey,
            clamped,
            UserSettingsPath);

        return GetSettings();
    }
}
