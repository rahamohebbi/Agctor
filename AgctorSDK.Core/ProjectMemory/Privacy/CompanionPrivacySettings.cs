namespace AgctorSDK.Core.ProjectMemory.Privacy;

/// <summary>PRD-022b: persisted under <c>.agctor/runtime/companion-privacy.yaml</c>.</summary>
public sealed class CompanionPrivacySettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>When false, PRD-021 session checkpoint/delete ingest is skipped.</summary>
    public bool AutoIngestOnSessionEnd { get; set; } = true;
}
