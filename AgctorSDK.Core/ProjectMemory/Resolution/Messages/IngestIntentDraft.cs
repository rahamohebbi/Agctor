using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Messages;

/// <summary>
/// Proposed change to a mention site produced by a resolution actor. The resolution subsystem
/// never writes narrative markdown itself; this draft is handed to an <see cref="IResolutionIntentSink"/>
/// that either materializes the change (via the existing PRD-016 ingest pipeline) or records it
/// as a sidecar so operators can see the proposal.
/// </summary>
public enum IntentKind
{
    /// <summary>Write a soft-link record next to the mention (non-canonical).</summary>
    SoftLink,
    /// <summary>Upgrade soft -&gt; hard canonical reference.</summary>
    HardLink,
    /// <summary>Downgrade hard -&gt; soft.</summary>
    Demote,
    /// <summary>Mark the edge as rejected.</summary>
    Reject
}

public sealed class IngestIntentDraft
{
    public string EdgeId { get; set; } = "";
    public IntentKind Kind { get; set; } = IntentKind.SoftLink;
    public MentionRef Mention { get; set; } = new();
    public string TargetEntityKey { get; set; } = "";
    public string TargetEntityPath { get; set; } = "";
    public double Confidence { get; set; }
    public string? Reason { get; set; }
}
