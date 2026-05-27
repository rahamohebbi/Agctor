using System.Text.Json;
using System.Text.Json.Serialization;
using System;

namespace AgctorSDK.Core.Sessions;

/// <summary>Project-level chat settings stored in SQLite <c>session_projects.settings_json</c>.</summary>
public sealed class ChatProjectSettings
{
    public const int DefaultVisualMaxPhotos = 3;
    public const int MaxVisualContextPhotosCap = 12;

    public int SchemaVersion { get; set; } = 1;

    /// <summary>How many recent catalog photos visual context may include (newest first).</summary>
    public int? VisualMaxPhotos { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ChatProjectSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ChatProjectSettings();

        try
        {
            return JsonSerializer.Deserialize<ChatProjectSettings>(json, JsonOpts) ?? new ChatProjectSettings();
        }
        catch
        {
            return new ChatProjectSettings();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static int ClampVisualMaxPhotos(int value, int maxCap = MaxVisualContextPhotosCap) =>
        Math.Clamp(value, 1, maxCap);

    /// <summary>Resolved cap: project override, else Host default, clamped to max.</summary>
    public static int ResolveVisualMaxPhotos(
        int? projectVisualMaxPhotos,
        int defaultValue = DefaultVisualMaxPhotos,
        int maxCap = MaxVisualContextPhotosCap)
    {
        var raw = projectVisualMaxPhotos ?? defaultValue;
        return ClampVisualMaxPhotos(raw, maxCap);
    }

    public int ResolveVisualMaxPhotos(int defaultValue = DefaultVisualMaxPhotos, int maxCap = MaxVisualContextPhotosCap) =>
        ResolveVisualMaxPhotos(VisualMaxPhotos, defaultValue, maxCap);

    public void ApplyVisualMaxPhotos(int? value, int maxCap = MaxVisualContextPhotosCap)
    {
        if (!value.HasValue)
            return;
        VisualMaxPhotos = ClampVisualMaxPhotos(value.Value, maxCap);
    }
}
