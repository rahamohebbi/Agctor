using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Review;

/// <summary>
/// Read-only projection of pending soft links for a review surface (Razor page, CLI, etc.).
/// Scans every known entity's <c>.resolution/incoming.yaml</c> and ranks by
/// <c>confidence * recency</c> — what PRD-018 §5.7 U3 calls the default review sort.
/// </summary>
public sealed class ResolutionReviewQuery
{
    private readonly IReadOnlyList<EntityRecord> _entities;

    public ResolutionReviewQuery(IReadOnlyList<EntityRecord> entities)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    }

    public IReadOnlyList<ReviewRow> Pending(int max = 50, double minConfidence = 0.0)
    {
        var rows = new List<ReviewRow>();
        foreach (var e in _entities)
        {
            var store = new ResolutionEdgeStore(e.RootPath);
            if (!File.Exists(ResolutionPaths.IncomingPath(e.RootPath))) continue;
            var doc = store.Load();
            foreach (var edge in doc.Edges)
            {
                if (edge.State != ResolutionEdgeState.Soft) continue;
                if (edge.Confidence < minConfidence) continue;
                rows.Add(new ReviewRow
                {
                    EntityKey = e.EntityKey,
                    EntityPath = e.RootPath,
                    Edge = edge
                });
            }
        }

        // Newer soft links with higher confidence bubble up; simple score keeps this deterministic.
        return rows
            .OrderByDescending(r => r.Edge.Confidence * RecencyWeight(r.Edge.LastUpdatedAt))
            .Take(Math.Max(1, max))
            .ToList();
    }

    private static double RecencyWeight(DateTimeOffset updated)
    {
        if (updated == default) return 0.1; // unknown timestamp: low but non-zero so confidence still orders
        var ageDays = (DateTimeOffset.UtcNow - updated).TotalDays;
        if (ageDays <= 0) return 1.0;
        // Half-life of ~14 days; exponential decay, floored to keep sort deterministic.
        return Math.Max(0.1, Math.Exp(-ageDays / 14.0));
    }

    public sealed class ReviewRow
    {
        public string EntityKey { get; set; } = "";
        public string EntityPath { get; set; } = "";
        public ResolutionEdge Edge { get; set; } = new();
    }
}
