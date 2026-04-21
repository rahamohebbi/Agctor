using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Bridge;

/// <summary>
/// Production bridge between the resolution subsystem and PRD-016's ingest pipeline.
/// Translates each <see cref="IngestIntentDraft"/> into a <c>memoryIntents</c>-shaped proposal
/// JSON file under <c>.agctor/runtime/resolution/intents/</c>, which the ingest runner can then
/// pick up (via an optional hook) and materialize through the existing projection path.
///
/// The sink is append-only from the subsystem's perspective — one file per draft — so operators
/// can review and/or replay proposals without mutating narrative markdown directly.
/// </summary>
/// <remarks>
/// Keeping materialization out-of-band avoids tangling the resolver mailbox with ingest locks.
/// An ingest runner that wants to auto-apply these can scan the folder, translate each row to
/// a <c>MemoryIntentBatch</c>, and feed it to <c>ProjectMemoryPipelineRunner</c>.
/// </remarks>
public sealed class MemoryIntentBridgeSink : IResolutionIntentSink
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _projectRoot;

    public MemoryIntentBridgeSink(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentNullException(nameof(projectRoot));
        _projectRoot = projectRoot;
    }

    public string Folder => Path.Combine(_projectRoot, ".agctor", "runtime", "resolution", "intents");

    public Task ApplyAsync(IngestIntentDraft draft, CancellationToken cancellationToken = default)
    {
        if (draft == null) return Task.CompletedTask;

        Directory.CreateDirectory(Folder);

        // Translate the link-style draft into the same shape the extractor emits so the existing
        // ingest pipeline can parse it without special-casing.
        var batch = new MemoryIntentsFile
        {
            Kind = "resolution-proposal",
            EdgeId = draft.EdgeId,
            IntentKind = draft.Kind.ToString(),
            MentionId = draft.Mention?.MentionId ?? "",
            WithinEntityKey = draft.Mention?.WithinEntityKey,
            SourcePath = draft.Mention?.SourcePath,
            TargetEntityKey = draft.TargetEntityKey,
            TargetEntityPath = draft.TargetEntityPath,
            Confidence = draft.Confidence,
            Reason = draft.Reason,
            RecordedAt = DateTimeOffset.UtcNow,
            MemoryIntents = new List<MemoryIntentRow>
            {
                new()
                {
                    EntityKey = draft.Mention?.WithinEntityKey ?? draft.TargetEntityKey,
                    KnowledgeType = MapKnowledgeType(draft.Kind),
                    Attribute = draft.Mention?.Field,
                    Value = FormatValue(draft),
                    Confidence = draft.Confidence
                }
            }
        };

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'fff");
        var safeEdge = SanitizeFileName(draft.EdgeId);
        var path = Path.Combine(Folder, $"{stamp}-{draft.Kind}-{safeEdge}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(batch, Json));
        return Task.CompletedTask;
    }

    private static string MapKnowledgeType(IntentKind kind) => kind switch
    {
        IntentKind.HardLink => "entityRef",
        IntentKind.Demote => "softLinkTo",
        IntentKind.Reject => "reject",
        _ => "softLinkTo"
    };

    private static string FormatValue(IngestIntentDraft draft) =>
        string.IsNullOrWhiteSpace(draft.TargetEntityPath)
            ? draft.TargetEntityKey
            : $"{draft.TargetEntityKey} ({draft.TargetEntityPath})";

    private static string SanitizeFileName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "edge";
        Span<char> buf = stackalloc char[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            buf[i] = (char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_';
        }
        return new string(buf).TrimStart('_').TrimEnd('_');
    }

    /// <summary>Shape serialized to disk so the ingest runner (or a test) can read it back.</summary>
    public sealed class MemoryIntentsFile
    {
        public string Kind { get; set; } = "resolution-proposal";
        public string EdgeId { get; set; } = "";
        public string IntentKind { get; set; } = "";
        public string MentionId { get; set; } = "";
        public string? WithinEntityKey { get; set; }
        public string? SourcePath { get; set; }
        public string TargetEntityKey { get; set; } = "";
        public string TargetEntityPath { get; set; } = "";
        public double Confidence { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public List<MemoryIntentRow> MemoryIntents { get; set; } = new();
    }

    public sealed class MemoryIntentRow
    {
        public string EntityKey { get; set; } = "";
        public string KnowledgeType { get; set; } = "";
        public string? Attribute { get; set; }
        public string Value { get; set; } = "";
        public double Confidence { get; set; }
    }
}

/// <summary>Fans a draft out to every registered sink. Useful during rollout: sidecar + bridge.</summary>
public sealed class CompositeResolutionIntentSink : IResolutionIntentSink
{
    private readonly IReadOnlyList<IResolutionIntentSink> _sinks;

    public CompositeResolutionIntentSink(IReadOnlyList<IResolutionIntentSink> sinks)
    {
        _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
    }

    public async Task ApplyAsync(IngestIntentDraft draft, CancellationToken cancellationToken = default)
    {
        foreach (var s in _sinks)
            await s.ApplyAsync(draft, cancellationToken).ConfigureAwait(false);
    }
}
