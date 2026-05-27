using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Sessions.Models;

/// <summary>Lightweight attachment reference stored on a <see cref="SessionTurn"/> (PRD-023b).</summary>
public sealed class SessionAttachmentEnvelope
{
    public string SchemaVersion { get; set; } = "1.0";

    public List<SessionAttachmentRef> Attachments { get; set; } = new();
}

public sealed class SessionAttachmentRef
{
    public string AssetId { get; set; } = "";

    public string Kind { get; set; } = "image";

    public string? Mime { get; set; }

    public string? FileName { get; set; }

    public string State { get; set; } = "uploaded";

    /// <summary>Populated when serving transcript (not stored in SQLite).</summary>
    public string? ViewUrl { get; set; }

    public DateTimeOffset? ViewUrlExpiresAt { get; set; }

    public string? Caption { get; set; }

    /// <summary>Subject slugs from visual asset catalog (enriched when loading transcript).</summary>
    public List<string>? EntityKeys { get; set; }

    /// <summary>Playground status line (enriched at read time; not stored in SQLite).</summary>
    public string? StatusDetail { get; set; }
}
