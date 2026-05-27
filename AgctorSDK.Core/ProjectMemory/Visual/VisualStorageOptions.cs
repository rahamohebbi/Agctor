namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>PRD-023: S3-compatible blob storage for person photos (MinIO locally).</summary>
public sealed class VisualStorageOptions
{
    /// <summary><c>s3</c> (default) or <c>file</c> (project-local dev without MinIO).</summary>
    public string Provider { get; set; } = "s3";

    public string Endpoint { get; set; } = "http://localhost:9000";

    public string Bucket { get; set; } = "agctor-visual";

    public string Region { get; set; } = "us-east-1";

    /// <summary>Optional; empty uses anonymous MinIO dev credentials.</summary>
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    public bool ForcePathStyle { get; set; } = true;

    public long MaxUploadBytes { get; set; } = 15 * 1024 * 1024;

    public int MaxAttachmentsPerTurn { get; set; } = 5;

    public int PresignedUploadExpirySeconds { get; set; } = 900;

    public int PresignedViewExpirySeconds { get; set; } = 900;

    /// <summary>Default recent-photo cap for visual context when project has no override.</summary>
    public int DefaultVisualContextPhotos { get; set; } = 3;

    /// <summary>Hard upper bound for project-level visualMaxPhotos setting.</summary>
    public int MaxVisualContextPhotos { get; set; } = 12;

    public string[] AllowedMimeTypes { get; set; } =
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heic"
    };
}
