using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Merges LLM default model into <c>appsettings.User.json</c> at content root (same file as project-memory user overrides).
/// </summary>
public sealed class LlmUserSettingsService : ILlmUserSettingsService
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<LlmUserSettingsService> _logger;

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public LlmUserSettingsService(IHostEnvironment environment, ILogger<LlmUserSettingsService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    private string UserSettingsPath => Path.Combine(_environment.ContentRootPath, "appsettings.User.json");

    public async Task PersistDefaultModelAsync(string model, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

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

        var llm = agctor["LLM"]?.AsObject() ?? new JsonObject();
        agctor["LLM"] = llm;
        llm["DefaultModel"] = model.Trim();

        var json = root.ToJsonString(JsonWriteOptions);
        await File.WriteAllTextAsync(UserSettingsPath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated Agctor:LLM:DefaultModel in {Path}", UserSettingsPath);
    }
}
