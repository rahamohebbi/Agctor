using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Persists the Ollama default model into <c>appsettings.json</c> and <c>appsettings.User.json</c>
/// so the dashboard choice matches the files operators edit (PRD-015).
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
    private string AppSettingsPath => Path.Combine(_environment.ContentRootPath, "appsettings.json");

    public async Task PersistDefaultModelAsync(string model, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var trimmed = model.Trim();

        // Base file: what most people open in the IDE.
        await MergeDefaultModelIntoFileAsync(AppSettingsPath, trimmed, createIfMissing: false, cancellationToken)
            .ConfigureAwait(false);

        // User overlay: keeps PRD-010 layering for other Agctor keys in the same file.
        await MergeDefaultModelIntoFileAsync(UserSettingsPath, trimmed, createIfMissing: true, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Updated Agctor:LLM:DefaultModel to {Model} in appsettings.json and appsettings.User.json",
            trimmed);
    }

    /// <summary>Merge <c>Agctor:LLM:DefaultModel</c> without touching unrelated keys.</summary>
    private static async Task MergeDefaultModelIntoFileAsync(
        string path,
        string model,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        JsonObject root;
        if (File.Exists(path))
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            root = JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        else if (createIfMissing)
        {
            root = new JsonObject();
        }
        else
        {
            return;
        }

        var agctor = root["Agctor"]?.AsObject() ?? new JsonObject();
        root["Agctor"] = agctor;

        var llm = agctor["LLM"]?.AsObject() ?? new JsonObject();
        agctor["LLM"] = llm;
        llm["DefaultModel"] = model;

        var json = root.ToJsonString(JsonWriteOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}
