namespace AgctorSDK.Host.Models;

public sealed class VisualAssetInitUploadRequestDto
{
    public string SchemaVersion { get; set; } = "1.0";

    public string? ProjectRoot { get; set; }

    public string ScenarioId { get; set; } = "";

    public string? SessionId { get; set; }

    public string? TurnGroupId { get; set; }

    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "image/jpeg";

    public long Bytes { get; set; }
}

public sealed class VisualAssetInitUploadResponseDto
{
    public string AssetId { get; set; } = "";

    public string UploadUrl { get; set; } = "";

    public Dictionary<string, string> UploadHeaders { get; set; } = new();

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary><c>s3</c> presigned PUT, or <c>file</c> for local dev upload-via-API.</summary>
    public string UploadMode { get; set; } = "s3";
}

public sealed class VisualAssetCompleteUploadRequestDto
{
    public string SchemaVersion { get; set; } = "1.0";

    public string? ProjectRoot { get; set; }

    public string ScenarioId { get; set; } = "";

    public string? Sha256 { get; set; }
}

public sealed class VisualAssetAnnotateRequestDto
{
    public string? ProjectRoot { get; set; }

    public string ScenarioId { get; set; } = "";

    public string EntityKey { get; set; } = "";

    public string? DisplayName { get; set; }
}

public sealed class VisualAssetDto
{
    public string AssetId { get; set; } = "";

    public string ScenarioId { get; set; } = "";

    public string State { get; set; } = "";

    public string ContentType { get; set; } = "";

    public long Bytes { get; set; }

    public string? ViewUrl { get; set; }

    public DateTimeOffset? ViewUrlExpiresAt { get; set; }

    /// <summary>Human-readable state for playground (e.g. vision unavailable).</summary>
    public string? StatusDetail { get; set; }

    /// <summary>Vision infer confidence (0–1) when available.</summary>
    public double? InferenceConfidence { get; set; }

    /// <summary>Subject entity keys from catalog (for clarify chips).</summary>
    public List<string>? SubjectEntityKeys { get; set; }
}
