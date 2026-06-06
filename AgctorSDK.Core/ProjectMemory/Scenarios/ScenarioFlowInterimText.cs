namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>User-visible assistant text while a v2 flow is suspended (before final Output).</summary>
public static class ScenarioFlowInterimText
{
    public static string? ForSnapshot(ScenarioFlowRuntimeSnapshot snapshot)
    {
        if (snapshot.Status == ScenarioFlowRuntimeStatus.WaitingForDomainEvent)
        {
            var eventType = snapshot.AwaitingEvent?.EventType?.Trim();
            if (string.Equals(eventType, ScenarioFlowDomainEventTypes.VisualExtractCompleted, StringComparison.OrdinalIgnoreCase))
            {
                return TryNodeText(snapshot, "n_visual")
                       ?? "Thanks for the photos — analyzing them now. Style advice will follow shortly.";
            }

            if (!string.IsNullOrWhiteSpace(eventType))
                return "Working on your request…";
        }

        if (snapshot.Status == ScenarioFlowRuntimeStatus.WaitingForUserInput
            && !string.IsNullOrWhiteSpace(snapshot.PendingPrompt))
        {
            return snapshot.PendingPrompt.Trim();
        }

        return TryNodeText(snapshot, snapshot.ExecutionNodeId)
               ?? LatestPersonaNodeText(snapshot);
    }

    public static string SuspendFallback(ScenarioFlowRuntimeStatus status) =>
        status switch
        {
            ScenarioFlowRuntimeStatus.WaitingForDomainEvent => "Working on your request…",
            ScenarioFlowRuntimeStatus.WaitingForUserInput => "Waiting for your input.",
            _ => "Waiting for your input."
        };

    private static string? TryNodeText(ScenarioFlowRuntimeSnapshot snapshot, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return null;
        return snapshot.Store.NodeOutputs.TryGetValue(nodeId.Trim(), out var output)
               && !string.IsNullOrWhiteSpace(output.Text)
            ? output.Text.Trim()
            : null;
    }

    private static string? LatestPersonaNodeText(ScenarioFlowRuntimeSnapshot snapshot)
    {
        foreach (var pair in snapshot.Store.NodeOutputs.Reverse())
        {
            if (!string.IsNullOrWhiteSpace(pair.Value.Text))
                return pair.Value.Text.Trim();
        }

        return null;
    }
}
