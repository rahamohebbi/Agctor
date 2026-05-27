using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.Visual.Models;

/// <summary>On-disk catalog entry for one photo under <c>scenarios/&lt;id&gt;/visual/assets/</c>.</summary>
public sealed class VisualAssetRecord
{
    public string SchemaVersion { get; set; } = "1.0";

    public string AssetId { get; set; } = "";

    public string ScenarioId { get; set; } = "";

    public string ProjectId { get; set; } = "";

    public VisualAssetStorageRef Storage { get; set; } = new();

    public string State { get; set; } = VisualAssetStates.PendingUpload;

    public DateTimeOffset? CapturedAt { get; set; }

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? UploadedBySessionId { get; set; }

    public string? SourceTurnGroupId { get; set; }

    public VisualAssetContext Context { get; set; } = new();

    public List<VisualAssetSubject> Subjects { get; set; } = new();

    public VisualAssetInference? Inference { get; set; }

    public VisualAssetPrivacy Privacy { get; set; } = new();

    public VisualAssetExtractionMeta Extraction { get; set; } = new();
}

public sealed class VisualAssetStorageRef
{
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

    public string ContentType { get; set; } = "image/jpeg";

    public string? Sha256 { get; set; }

    public long Bytes { get; set; }
}

public sealed class VisualAssetContext
{
    public string? UserCaption { get; set; }

    public string? Occasion { get; set; }
}

public sealed class VisualAssetSubject
{
    public string EntityKey { get; set; } = "";

    public string Role { get; set; } = "primary";

    public string? DisplayName { get; set; }
}

public sealed class VisualAssetInference
{
    public string Source { get; set; } = "prompt";

    public double Confidence { get; set; }

    public List<string> EntityKeys { get; set; } = new();

    public string? Rationale { get; set; }
}

public sealed class VisualAssetPrivacy
{
    public string Sensitivity { get; set; } = "normal";

    public List<string> AllowAgentUse { get; set; } = new() { "general", "style", "fitness", "relationship" };
}

public sealed class VisualAssetExtractionMeta
{
    public string Status { get; set; } = "pending";

    /// <summary>One-sentence visible scene/activity description from vision extract or query fallback.</summary>
    public string? SceneSummary { get; set; }

    public string? OllamaModel { get; set; }

    public string? PromptVersion { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }
}

public static class VisualAssetStates
{
    public const string PendingUpload = "pending_upload";
    public const string Uploaded = "uploaded";
    public const string Inferring = "inferring";
    public const string ReadyForExtract = "ready_for_extract";
    public const string Extracting = "extracting";
    public const string Extracted = "extracted";
    public const string InboxPending = "inbox_pending";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Deleted = "deleted";
}
