using System.Text.Json;
using AgctorSDK.Core.Workflows;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed class SceneVideoTopologyOptions
{
    /// <summary>Path relative to content root.</summary>
    public string File { get; set; } = "Config/scene-video-topology.json";
}

/// <summary>Loads the fixed scene-video topology definition (Phase A; read-only for editors).</summary>
public interface ISceneVideoTopologyCatalog
{
    SceneVideoTopologyDocument Get();
    SceneVideoTopologyDocument? GetForScenario(string scenarioId);
}

public sealed class SceneVideoTopologyCatalog : ISceneVideoTopologyCatalog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly SceneVideoTopologyDocument _document;
    private readonly ILogger<SceneVideoTopologyCatalog> _log;

    public SceneVideoTopologyCatalog(
        IWebHostEnvironment env,
        IOptions<SceneVideoTopologyOptions> options,
        ILogger<SceneVideoTopologyCatalog> log)
    {
        _log = log;
        var path = Path.Combine(env.ContentRootPath, options.Value.File);
        if (!File.Exists(path))
            throw new FileNotFoundException("Scene video topology file is missing.", path);
        var raw = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<SceneVideoTopologyDocument>(raw, Json)
            ?? throw new InvalidDataException("Scene video topology JSON was empty.");
        var errors = SceneVideoTopologyValidator.Validate(doc);
        if (errors.Count > 0)
        {
            foreach (var error in errors) _log.LogError("Scene video topology: {Error}", error);
            throw new InvalidDataException("Scene video topology failed validation: " + string.Join(" ", errors));
        }
        _document = doc;
    }

    public SceneVideoTopologyDocument Get() => Clone(_document);

    public SceneVideoTopologyDocument? GetForScenario(string scenarioId) =>
        string.Equals(scenarioId, "scene-video-chain", StringComparison.Ordinal)
            ? Get()
            : null;

    private static SceneVideoTopologyDocument Clone(SceneVideoTopologyDocument src) =>
        JsonSerializer.Deserialize<SceneVideoTopologyDocument>(JsonSerializer.Serialize(src, Json), Json)!;
}
