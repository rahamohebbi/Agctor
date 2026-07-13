using System.Text.Json;
using System.Text.Json.Nodes;
using AgctorSDK.Core.Rag;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Writes RAG provider settings to appsettings.User.json (same pattern as <see cref="UserRuntimeSettingsService"/>).
/// </summary>
public sealed class UserRagSettingsService : IUserRagSettingsService
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<UserRagSettingsService> _logger;

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public UserRagSettingsService(IHostEnvironment environment, ILogger<UserRagSettingsService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    private string UserSettingsPath => Path.Combine(_environment.ContentRootPath, "appsettings.User.json");

    /// <inheritdoc />
    public async Task PersistAsync(RagSettingsUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var canonical = RagProviderIds.Normalize(update.DefaultProvider);

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

        var rag = agctor["Rag"]?.AsObject() ?? new JsonObject();
        agctor["Rag"] = rag;
        rag["DefaultProvider"] = canonical;

        if (update.LightRAG != null)
            rag["LightRAG"] = JsonSerializer.SerializeToNode(update.LightRAG, JsonWriteOptions);

        if (update.Graphiti != null)
            rag["Graphiti"] = JsonSerializer.SerializeToNode(update.Graphiti, JsonWriteOptions);

        if (update.Cognee != null)
            rag["Cognee"] = JsonSerializer.SerializeToNode(update.Cognee, JsonWriteOptions);

        var json = root.ToJsonString(JsonWriteOptions);
        await File.WriteAllTextAsync(UserSettingsPath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Updated RAG provider selection: DefaultProvider={Provider} in {Path}",
            canonical,
            UserSettingsPath);
    }
}
