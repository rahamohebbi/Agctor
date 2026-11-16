using System.Text.Json;
using System.Text.Json.Serialization;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.Scenarios;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.VideoWorkflow;

public sealed class SceneVideoTopologyOptions
{
    /// <summary>Path relative to content root (shipped roster).</summary>
    public string File { get; set; } = "Config/scene-video-topology.json";
    /// <summary>Optional overlay for Phase D edits (audio + layouts).</summary>
    public string UserFile { get; set; } = "Config/scene-video-topology.user.json";
}

/// <summary>v1 editable fields: audio enable + shared node layouts.</summary>
public sealed class SceneVideoTopologyEditRequest
{
    public bool AudioEnabled { get; set; } = true;
    public Dictionary<string, SceneVideoTopologyLayout>? NodeLayouts { get; set; }
}

/// <summary>Loads and persists the scene-video topology definition (Phase D: layout + audio).</summary>
public interface ISceneVideoTopologyCatalog
{
    SceneVideoTopologyDocument Get();
    SceneVideoTopologyDocument? GetForScenario(string scenarioId);
    /// <summary>Apply v1 edit (audio + layouts), validate, and persist the user overlay.</summary>
    SceneVideoTopologyDocument SaveEdit(string scenarioId, SceneVideoTopologyEditRequest edit);
}

public sealed class SceneVideoTopologyCatalog : ISceneVideoTopologyCatalog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly SceneVideoTopologyDocument _baseline;
    private SceneVideoTopologyDocument _document;
    private readonly string _userPath;
    private readonly IScenarioCatalog _scenarios;
    private readonly ILogger<SceneVideoTopologyCatalog> _log;
    private bool _admissionMigrated;

    public SceneVideoTopologyCatalog(
        IWebHostEnvironment env,
        IOptions<SceneVideoTopologyOptions> options,
        IScenarioCatalog scenarios,
        ILogger<SceneVideoTopologyCatalog> log)
    {
        _log = log;
        _scenarios = scenarios;
        var path = ResolvePath(env.ContentRootPath, options.Value.File);
        _userPath = ResolvePath(env.ContentRootPath, options.Value.UserFile);
        if (!File.Exists(path))
            throw new FileNotFoundException("Scene video topology file is missing.", path);
        var raw = File.ReadAllText(path);
        _baseline = JsonSerializer.Deserialize<SceneVideoTopologyDocument>(raw, Json)
            ?? throw new InvalidDataException("Scene video topology JSON was empty.");
        var errors = SceneVideoTopologyValidator.Validate(_baseline);
        if (errors.Count > 0)
        {
            foreach (var error in errors) _log.LogError("Scene video topology: {Error}", error);
            throw new InvalidDataException("Scene video topology failed validation: " + string.Join(" ", errors));
        }
        _document = ApplyUserOverlay(Clone(_baseline), LoadUserOverlay());
        errors = SceneVideoTopologyValidator.Validate(_document);
        if (errors.Count > 0)
            throw new InvalidDataException("Scene video topology overlay failed validation: " + string.Join(" ", errors));
    }

    public SceneVideoTopologyDocument Get()
    {
        lock (_gate)
        {
            EnsureAdmissionMigrated();
            return Clone(_document);
        }
    }

    public SceneVideoTopologyDocument? GetForScenario(string scenarioId) =>
        string.Equals(scenarioId, "scene-video-chain", StringComparison.Ordinal)
            ? Get()
            : null;

    public SceneVideoTopologyDocument SaveEdit(string scenarioId, SceneVideoTopologyEditRequest edit)
    {
        if (!string.Equals(scenarioId, "scene-video-chain", StringComparison.Ordinal))
            throw new KeyNotFoundException($"Scenario '{scenarioId}' has no agent topology document yet.");
        ArgumentNullException.ThrowIfNull(edit);

        lock (_gate)
        {
            var next = Clone(_baseline);
            // Keep prior user layouts when the request omits them; always apply audio flag.
            var priorLayouts = _document.Ui?.NodeLayouts;
            next.AudioEnabled = edit.AudioEnabled;
            next.Ui ??= new SceneVideoTopologyUi();
            next.Ui.NodeLayouts = edit.NodeLayouts != null
                ? new Dictionary<string, SceneVideoTopologyLayout>(edit.NodeLayouts, StringComparer.Ordinal)
                : priorLayouts != null
                    ? new Dictionary<string, SceneVideoTopologyLayout>(priorLayouts, StringComparer.Ordinal)
                    : next.Ui.NodeLayouts;

            var errors = SceneVideoTopologyValidator.ValidateFixedRosterEdit(_baseline, next);
            if (errors.Count > 0)
                throw new ArgumentException(string.Join("\n", errors));

            WriteOverlay(new SceneVideoTopologyEditRequest
            {
                AudioEnabled = next.AudioEnabled,
                NodeLayouts = next.Ui?.NodeLayouts
            });
            _document = next;
            _admissionMigrated = true;
            _log.LogInformation("Saved scene-video topology edit (audioEnabled={Audio})", next.AudioEnabled);
            return Clone(_document);
        }
    }

    /// <summary>
    /// One-shot: if the operator never saved a topology overlay, copy Audio Engineer enable from the
    /// legacy admission flow so edit/live match pre-Phase-D customizations.
    /// </summary>
    private void EnsureAdmissionMigrated()
    {
        if (_admissionMigrated || File.Exists(_userPath))
        {
            _admissionMigrated = true;
            return;
        }

        _admissionMigrated = true;
        var flow = _scenarios.Get("scene-video-chain")?.Flow;
        if (!SceneVideoTopologyMigration.TrySeedAudioFromAdmission(_document, flow, out var audio))
            return;

        WriteOverlay(new SceneVideoTopologyEditRequest
        {
            AudioEnabled = audio,
            NodeLayouts = _document.Ui?.NodeLayouts
        });
        _log.LogInformation(
            "Migrated scene-video topology.audioEnabled={Audio} from admission flow (no prior topology overlay)",
            audio);
    }

    private void WriteOverlay(SceneVideoTopologyEditRequest overlay)
    {
        var dir = Path.GetDirectoryName(_userPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_userPath, JsonSerializer.Serialize(overlay, Json));
    }

    private SceneVideoTopologyEditRequest? LoadUserOverlay()
    {
        if (!File.Exists(_userPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<SceneVideoTopologyEditRequest>(File.ReadAllText(_userPath), Json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ignoring unreadable scene-video topology user overlay at {Path}", _userPath);
            return null;
        }
    }

    private static SceneVideoTopologyDocument ApplyUserOverlay(
        SceneVideoTopologyDocument doc, SceneVideoTopologyEditRequest? overlay)
    {
        if (overlay == null) return doc;
        doc.AudioEnabled = overlay.AudioEnabled;
        if (overlay.NodeLayouts != null)
        {
            doc.Ui ??= new SceneVideoTopologyUi();
            doc.Ui.NodeLayouts = new Dictionary<string, SceneVideoTopologyLayout>(overlay.NodeLayouts, StringComparer.Ordinal);
        }
        return doc;
    }

    private static string ResolvePath(string root, string configured) =>
        Path.IsPathRooted(configured) ? configured : Path.GetFullPath(Path.Combine(root, configured ?? string.Empty));

    private static SceneVideoTopologyDocument Clone(SceneVideoTopologyDocument src) =>
        JsonSerializer.Deserialize<SceneVideoTopologyDocument>(JsonSerializer.Serialize(src, Json), Json)!;
}
