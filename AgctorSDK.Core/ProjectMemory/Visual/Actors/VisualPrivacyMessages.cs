namespace AgctorSDK.Core.ProjectMemory.Visual.Actors;

/// <summary>Actor protocol: purge visual assets for one person (PRD-023f).</summary>
public sealed record VisualPrivacyPurgeRequest(
    string ProjectRoot,
    string ScenarioId,
    string EntityKey);

public sealed record VisualPrivacyPurgeWorkflowResult(
    bool Success,
    int AssetsRemoved,
    int BlobsDeleted,
    string? Error);
