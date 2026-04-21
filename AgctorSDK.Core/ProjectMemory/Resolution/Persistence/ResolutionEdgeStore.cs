using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Persistence;

/// <summary>
/// File container for one entity's <c>incoming.yaml</c>. YAML-friendly wrapper around a list of edges.
/// </summary>
public sealed class IncomingEdges
{
    public List<ResolutionEdge> Edges { get; set; } = new();
}

/// <summary>
/// Disk-backed store for a single entity's resolution state. The store is the *only* place that
/// touches <c>.resolution/</c> YAML; actors delegate here to keep persistence testable without
/// spinning up a runtime.
/// </summary>
/// <remarks>
/// Writes are atomic via write-to-temp-then-rename. The promotions log is append-only: each call
/// to <see cref="AppendPromotion"/> adds one YAML document (separated by <c>---</c>) so the file
/// diffs cleanly in git.
/// </remarks>
public sealed class ResolutionEdgeStore
{
    private readonly string _entityRoot;

    public ResolutionEdgeStore(string entityRootPath)
    {
        if (string.IsNullOrWhiteSpace(entityRootPath))
            throw new ArgumentException("Entity root path is required", nameof(entityRootPath));
        _entityRoot = entityRootPath;
    }

    public string EntityRoot => _entityRoot;

    /// <summary>
    /// Load all incoming edges for this entity. Returns an empty list when no sidecar exists yet.
    /// </summary>
    public IncomingEdges Load()
    {
        var path = ResolutionPaths.IncomingPath(_entityRoot);
        if (!File.Exists(path))
            return new IncomingEdges();

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return new IncomingEdges();

        var parsed = ProjectYamlSerializer.Deserialize<IncomingEdges>(text);
        return parsed ?? new IncomingEdges();
    }

    /// <summary>
    /// Upsert an edge by <see cref="ResolutionEdge.EdgeId"/> and persist. Signals already present
    /// with the same <c>(kind, inputsFingerprint)</c> are not duplicated.
    /// </summary>
    public void Upsert(ResolutionEdge edge)
    {
        if (edge == null) throw new ArgumentNullException(nameof(edge));
        if (string.IsNullOrWhiteSpace(edge.EdgeId))
            throw new ArgumentException("EdgeId required", nameof(edge));

        var doc = Load();
        var existing = doc.Edges.FindIndex(e => e.EdgeId == edge.EdgeId);
        if (existing >= 0)
        {
            doc.Edges[existing] = MergeSignals(doc.Edges[existing], edge);
        }
        else
        {
            doc.Edges.Add(edge);
        }

        WriteIncoming(doc);
    }

    /// <summary>
    /// Append one promotion row to the audit log. Append-only on purpose; history is the point.
    /// </summary>
    public void AppendPromotion(string edgeId, ResolutionPromotion promotion)
    {
        Directory.CreateDirectory(ResolutionPaths.EntityResolutionFolder(_entityRoot));
        var path = ResolutionPaths.PromotionsPath(_entityRoot);
        var block = new PromotionRow { EdgeId = edgeId, Promotion = promotion };
        var yaml = ProjectYamlSerializer.Serialize(block);

        using var sw = new StreamWriter(path, append: true);
        sw.WriteLine("---");
        sw.Write(yaml);
    }

    private void WriteIncoming(IncomingEdges doc)
    {
        Directory.CreateDirectory(ResolutionPaths.EntityResolutionFolder(_entityRoot));
        var path = ResolutionPaths.IncomingPath(_entityRoot);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ProjectYamlSerializer.Serialize(doc));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    /// <summary>
    /// Merge signals from <paramref name="incoming"/> into <paramref name="existing"/> keeping
    /// idempotency on <c>(kind, inputsFingerprint)</c>. Preserves CreatedAt but refreshes
    /// LastUpdatedAt.
    /// </summary>
    private static ResolutionEdge MergeSignals(ResolutionEdge existing, ResolutionEdge incoming)
    {
        var byKey = new HashSet<string>(
            existing.Signals.Select(FingerprintKey),
            StringComparer.Ordinal);

        foreach (var s in incoming.Signals)
        {
            if (byKey.Add(FingerprintKey(s)))
                existing.Signals.Add(s);
        }

        existing.State = incoming.State;
        existing.Confidence = incoming.Confidence;
        existing.TargetEntityKey = incoming.TargetEntityKey;
        existing.Mention = incoming.Mention;
        existing.LastUpdatedAt = DateTimeOffset.UtcNow;
        return existing;
    }

    private static string FingerprintKey(ResolutionSignal s) => $"{s.Kind}|{s.InputsFingerprint}";

    private sealed class PromotionRow
    {
        public string EdgeId { get; set; } = "";
        public ResolutionPromotion Promotion { get; set; } = new();
    }
}
