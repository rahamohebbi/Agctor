using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Review;

/// <summary>
/// Read-only helper for renderers (playground transcript, person-query answers, assistant
/// footnotes) that need to decorate plain text with the resolution grade of mentioned surfaces.
/// Reuses the on-disk edge store so nothing here mutates state.
/// </summary>
/// <remarks>
/// Implements PRD-018 §5.7 U2 (inline markers), U4 (query grade), and the Phase 4 honest-narration
/// hook in one place so the three renderers agree.
/// </remarks>
public sealed class ResolutionAnnotator
{
    public sealed class SurfaceGrade
    {
        public string SurfaceForm { get; set; } = "";
        public string TargetEntityKey { get; set; } = "";
        public string TargetEntityPath { get; set; } = "";
        public double Confidence { get; set; }
        public ResolutionEdgeState State { get; set; }
    }

    private readonly Dictionary<string, SurfaceGrade> _bySurface = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Build an annotator from every entity's <c>.resolution/incoming.yaml</c>.</summary>
    public static ResolutionAnnotator FromEntities(IEnumerable<(string EntityKey, string DisplayName, string RootPath)> entities)
    {
        var a = new ResolutionAnnotator();
        if (entities == null) return a;
        foreach (var e in entities)
        {
            var path = ResolutionPaths.IncomingPath(e.RootPath);
            if (!File.Exists(path)) continue;
            var doc = new ResolutionEdgeStore(e.RootPath).Load();
            foreach (var edge in doc.Edges)
            {
                if (edge.State == ResolutionEdgeState.Rejected || edge.State == ResolutionEdgeState.Superseded) continue;
                var surface = edge.Mention?.SurfaceForm;
                if (string.IsNullOrWhiteSpace(surface)) continue;
                // Retain the strongest grade seen for this surface so aggregates read predictably.
                if (a._bySurface.TryGetValue(surface, out var existing) && existing.Confidence >= edge.Confidence && existing.State >= edge.State)
                    continue;
                a._bySurface[surface] = new SurfaceGrade
                {
                    SurfaceForm = surface,
                    TargetEntityKey = edge.TargetEntityKey,
                    TargetEntityPath = e.RootPath,
                    Confidence = edge.Confidence,
                    State = edge.State
                };
            }
        }
        return a;
    }

    public IReadOnlyCollection<SurfaceGrade> All => _bySurface.Values;

    public SurfaceGrade? Find(string surface) =>
        !string.IsNullOrWhiteSpace(surface) && _bySurface.TryGetValue(surface, out var g) ? g : null;

    /// <summary>
    /// Inline-markup helper: `Raha` becomes `Raha (soft-linked 72% → people/raha)` for soft edges
    /// and `Raha (→ people/raha)` for hard edges. Leaves unknown surfaces untouched.
    /// </summary>
    public string AnnotateInline(string text)
    {
        if (string.IsNullOrEmpty(text) || _bySurface.Count == 0) return text ?? "";
        // Simple literal replacement: every first occurrence of a surface gets a footnote.
        // Intentionally conservative — we'd need a proper tokenizer for aggressive markup.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = _bySurface.Keys.OrderByDescending(k => k.Length); // longer surfaces first to avoid substring shadowing
        var buffer = text;
        foreach (var surface in ordered)
        {
            if (!_bySurface.TryGetValue(surface, out var grade)) continue;
            var idx = buffer.IndexOf(surface, StringComparison.Ordinal);
            if (idx < 0) continue;
            if (!seen.Add(surface)) continue;
            var replacement = surface + " " + GradeFootnote(grade);
            buffer = buffer.Substring(0, idx) + replacement + buffer.Substring(idx + surface.Length);
        }
        return buffer;
    }

    /// <summary>Short footnote the assistant can append verbatim (PRD-018 §Phase 4 honest narration).</summary>
    public static string GradeFootnote(SurfaceGrade grade)
    {
        var pct = (int)Math.Round(grade.Confidence * 100);
        return grade.State switch
        {
            ResolutionEdgeState.Hard => $"(→ {grade.TargetEntityKey})",
            ResolutionEdgeState.Soft => $"(soft-linked {pct}% → {grade.TargetEntityKey})",
            _ => ""
        };
    }
}
