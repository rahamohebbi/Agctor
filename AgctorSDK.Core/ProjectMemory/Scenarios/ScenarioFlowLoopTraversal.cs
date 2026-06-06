namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>PRD-024 loop region attempts and store invalidation on loopBack.</summary>
public static class ScenarioFlowLoopTraversal
{
    public const string InvalidationFromTargetForward = "fromTargetForward";
    public const string InvalidationKeepAll = "keepAll";
    public const string InvalidationIterationScopeOnly = "iterationScopeOnly";

    public static ScenarioFlowLoopRegionState GetOrCreateRegion(
        ScenarioFlowRuntimeSnapshot snapshot,
        string regionId,
        int maxAttempts)
    {
        var existing = snapshot.LoopRegions.FirstOrDefault(r =>
            string.Equals(r.RegionId, regionId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var created = new ScenarioFlowLoopRegionState
        {
            RegionId = regionId,
            Attempt = 0,
            MaxAttempts = maxAttempts
        };
        snapshot.LoopRegions.Add(created);
        return created;
    }

    /// <returns>Error message when attempts exhausted; null when ok to proceed.</returns>
    public static string? TryIncrementAttempt(ScenarioFlowLoopRegionState region, bool incrementAttempt)
    {
        if (!incrementAttempt)
            return null;

        region.Attempt++;
        if (region.Attempt > region.MaxAttempts)
            return $"Loop region '{region.RegionId}' exceeded max attempts ({region.MaxAttempts}).";

        return null;
    }

    public static void ApplyStoreInvalidation(
        ScenarioFlowRuntimeSnapshot snapshot,
        string targetNodeId,
        IReadOnlyList<string> orderedNodeIds,
        string invalidationPolicy)
    {
        if (string.Equals(invalidationPolicy, InvalidationKeepAll, StringComparison.OrdinalIgnoreCase))
            return;

        var targetIndex = IndexOfNode(orderedNodeIds, targetNodeId);
        if (targetIndex < 0)
            return;

        var toClear = orderedNodeIds.Skip(targetIndex).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(invalidationPolicy, InvalidationIterationScopeOnly, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var key in snapshot.Store.NodeOutputs.Keys.ToList())
            {
                if (snapshot.Store.NodeOutputs.TryGetValue(key, out var output)
                    && string.Equals(output.Scope, "iteration", StringComparison.OrdinalIgnoreCase)
                    && toClear.Contains(key))
                {
                    snapshot.Store.NodeOutputs.Remove(key);
                }
            }

            return;
        }

        // fromTargetForward: clear run + iteration scoped outputs from target forward.
        foreach (var key in snapshot.Store.NodeOutputs.Keys.ToList())
        {
            if (!toClear.Contains(key))
                continue;

            if (snapshot.Store.NodeOutputs.TryGetValue(key, out var output)
                && string.Equals(output.Scope, "session", StringComparison.OrdinalIgnoreCase))
                continue;

            snapshot.Store.NodeOutputs.Remove(key);
        }
    }

    public static void MergeAttachmentDelta(ScenarioFlowRuntimeSnapshot snapshot, IReadOnlyList<string> attachmentIds)
    {
        if (attachmentIds.Count == 0)
            return;

        snapshot.Store.Attachments.NewSinceLastResume = attachmentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var all = snapshot.Store.Attachments.AllInRun.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in snapshot.Store.Attachments.NewSinceLastResume)
            all.Add(id);
        snapshot.Store.Attachments.AllInRun = all.ToList();

        snapshot.Store.Facts["user.hasAttachments"] = snapshot.Store.Attachments.AllInRun.Count > 0;
    }

    public static void ClearAttachmentDelta(ScenarioFlowRuntimeSnapshot snapshot)
    {
        snapshot.Store.Attachments.NewSinceLastResume = new List<string>();
    }

    private static int IndexOfNode(IReadOnlyList<string> orderedNodeIds, string nodeId)
    {
        for (var i = 0; i < orderedNodeIds.Count; i++)
        {
            if (string.Equals(orderedNodeIds[i], nodeId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
