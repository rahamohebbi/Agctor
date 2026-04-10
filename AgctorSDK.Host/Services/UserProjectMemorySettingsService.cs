using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgctorSDK.Host.Services;

public sealed class UserProjectMemorySettingsService : IUserProjectMemorySettingsService
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<UserProjectMemorySettingsService> _logger;

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public UserProjectMemorySettingsService(IHostEnvironment environment, ILogger<UserProjectMemorySettingsService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    private string UserSettingsPath => Path.Combine(_environment.ContentRootPath, "appsettings.User.json");

    public async Task PersistProjectRootAsync(string absoluteProjectRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteProjectRoot);
        var path = Path.GetFullPath(absoluteProjectRoot.Trim());

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
        pm["ProjectRoot"] = path;

        var json = root.ToJsonString(JsonWriteOptions);
        await File.WriteAllTextAsync(UserSettingsPath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated ProjectMemory:ProjectRoot in {Path}", UserSettingsPath);
    }
}
