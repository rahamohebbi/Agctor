using System.Text.Json;
using AgctorSDK.Core.SceneMemory;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>
/// Matches generated narration to the original spoken window.
/// Scene Memory dialogue ranges say where the line sits. Word times say how fast it is spoken.
/// The language model does not choose a speed.
/// </summary>
public static class SceneNarrationPace
{
    public const double DefaultStartSeconds = 0.2;

    public readonly record struct Placement(double Speed, double StartSeconds, double? TargetSeconds);

    /// <summary>
    /// Speed is measuredSeconds / target when a window exists. Faster than 1 means the line
    /// cannot match the original pace without speeding up; the caller rejects that.
    /// A pace override replaces the measured speed. Missing times and no dialogue range keep speed 1 and start 0.2.
    /// </summary>
    public static Placement Resolve(IReadOnlyList<SceneSpeechWord>? words, SceneTimeRange? dialogue, double measuredSeconds, double? paceOverride)
    {
        double? wordStart = null, wordEnd = null;
        if (words is { Count: > 0 })
        {
            wordStart = words.Min(w => w.StartSeconds);
            wordEnd = words.Max(w => w.EndSeconds);
        }
        var hasWords = wordStart is { } start && wordEnd is { } end && end > start && double.IsFinite(start) && double.IsFinite(end);
        double? target = hasWords ? wordEnd!.Value - wordStart!.Value : null;
        if (target is null && dialogue != null)
        {
            var length = (double)(dialogue.EndSeconds - dialogue.StartSeconds);
            if (length > 0) target = length;
        }
        var place = dialogue != null ? (double)dialogue.StartSeconds : hasWords ? wordStart!.Value : DefaultStartSeconds;
        if (paceOverride is > 0 and <= 1.5)
            return new(paceOverride.Value, place, target);
        if (target is null || measuredSeconds <= 0 || !double.IsFinite(measuredSeconds))
            return new(1, place, target);
        return new(measuredSeconds / target.Value, place, target);
    }

    public static SceneTimeRange? PlannedDialogue(SceneWorkspace? workspace, string shotId) =>
        Dialogue(workspace, shotId, "planned", null);
    public static SceneTimeRange? ConfirmedDialogue(SceneWorkspace? workspace, string shotId, string takeId) =>
        Dialogue(workspace, shotId, "confirmed", takeId);
    private static SceneTimeRange? Dialogue(SceneWorkspace? workspace, string shotId, string status, string? takeId)
    {
        var events = workspace?.Prompts?.FirstOrDefault(p => p.Id == shotId)?.TimedEvents;
        return events?.Where(e => e.Kind == "dialogue" && e.Status == status && (takeId == null || e.TakeId == takeId))
            .OrderBy(e => e.Timing.StartSeconds).FirstOrDefault()?.Timing;
    }

    public static SceneNarrationWindow Window(IReadOnlyList<SceneSpeechWord>? words, SceneTimeRange? dialogue, double? paceOverride) 
    {
        var placed = Resolve(words, dialogue, 1, null);
        return new(placed.TargetSeconds, placed.StartSeconds, paceOverride is > 0 and <= 1.5 ? paceOverride : null);
    }

    /// <summary>Approval may carry voice settings as JSON. Any other text is a script revision, not settings.</summary>
    public static bool TryVoiceSettings(string? text, out double? softness, out string? accent, out double? paceOverride)
    {
        softness = null; accent = null; paceOverride = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            var root = doc.RootElement;
            if (root.TryGetProperty("softness", out var soft) && soft.ValueKind == JsonValueKind.Number)
                softness = soft.GetDouble();
            if (root.TryGetProperty("accent", out var accentValue) && accentValue.ValueKind == JsonValueKind.String)
                accent = accentValue.GetString();
            if (root.TryGetProperty("paceOverride", out var pace) && pace.ValueKind == JsonValueKind.Number)
                paceOverride = pace.GetDouble();
            if (softness is < 0 or > 1 || paceOverride is <= 0 or > 1.5 || accent?.Length > 80) return false;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
