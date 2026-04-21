using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Bridge;

/// <summary>
/// Safe-by-default sink that records outgoing link proposals beside the mention's host entity:
/// <c>&lt;mentionHostRoot&gt;/.resolution/outgoing.yaml</c>. Does not touch narrative markdown.
/// Ideal during rollout: operators can inspect / git-diff the proposals before the production
/// <see cref="IResolutionIntentSink"/> is wired in.
/// </summary>
public sealed class SidecarIntentSink : IResolutionIntentSink
{
    private readonly Func<string, string?> _hostRootResolver;

    /// <param name="hostRootResolver">
    /// Given a mention's <c>WithinEntityKey</c>, return the absolute folder that should hold the
    /// <c>.resolution/outgoing.yaml</c>. Returning null skips the write (unknown host).
    /// </param>
    public SidecarIntentSink(Func<string, string?> hostRootResolver)
    {
        _hostRootResolver = hostRootResolver ?? throw new ArgumentNullException(nameof(hostRootResolver));
    }

    public Task ApplyAsync(IngestIntentDraft draft, CancellationToken cancellationToken = default)
    {
        var host = draft.Mention?.WithinEntityKey;
        if (string.IsNullOrWhiteSpace(host)) return Task.CompletedTask;

        var hostRoot = _hostRootResolver(host);
        if (string.IsNullOrWhiteSpace(hostRoot)) return Task.CompletedTask;

        var path = Path.Combine(hostRoot, ResolutionPaths.ResolutionFolder, "outgoing.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var doc = LoadExisting(path);
        var rowId = $"{draft.EdgeId}|{draft.Kind}";
        var existing = doc.Rows.FindIndex(r => r.RowId == rowId);
        var row = new OutgoingRow
        {
            RowId = rowId,
            EdgeId = draft.EdgeId,
            Kind = draft.Kind.ToString(),
            Mention = draft.Mention ?? new Models.MentionRef(),
            TargetEntityKey = draft.TargetEntityKey,
            TargetEntityPath = draft.TargetEntityPath,
            Confidence = draft.Confidence,
            Reason = draft.Reason,
            RecordedAt = DateTimeOffset.UtcNow
        };
        if (existing >= 0) doc.Rows[existing] = row;
        else doc.Rows.Add(row);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ProjectYamlSerializer.Serialize(doc));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);

        return Task.CompletedTask;
    }

    private static OutgoingDoc LoadExisting(string path)
    {
        if (!File.Exists(path)) return new OutgoingDoc();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new OutgoingDoc();
        return ProjectYamlSerializer.Deserialize<OutgoingDoc>(text) ?? new OutgoingDoc();
    }

    public sealed class OutgoingDoc
    {
        public List<OutgoingRow> Rows { get; set; } = new();
    }

    public sealed class OutgoingRow
    {
        public string RowId { get; set; } = "";
        public string EdgeId { get; set; } = "";
        public string Kind { get; set; } = "";
        public Models.MentionRef Mention { get; set; } = new();
        public string TargetEntityKey { get; set; } = "";
        public string TargetEntityPath { get; set; } = "";
        public double Confidence { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
    }
}
