namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>PRD-024 v2 flow detection and shared node/edge type constants.</summary>
public static class ScenarioFlowCapabilities
{
    public const string SchemaV2 = "2.0";

    public static readonly HashSet<string> V2NodeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Gate",
        "WaitForInput",
        "AwaitEvent",
        "Notify"
    };

    public static bool RequiresRuntimeActor(string? schemaVersion, IEnumerable<string> nodeTypes, IEnumerable<string> edgeModes)
    {
        if (IsSchemaV2(schemaVersion))
            return true;

        if (nodeTypes.Any(t => V2NodeTypes.Contains(t)))
            return true;

        return edgeModes.Any(m => string.Equals(m, "loopBack", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSchemaV2(string? schemaVersion) =>
        string.Equals(schemaVersion?.Trim(), SchemaV2, StringComparison.OrdinalIgnoreCase);

    public static bool IsSuspendStatus(ScenarioFlowRuntimeStatus status) =>
        status is ScenarioFlowRuntimeStatus.WaitingForUserInput
            or ScenarioFlowRuntimeStatus.WaitingForDomainEvent;

    /// <summary>Stable actor id for session + scenario pair.</summary>
    public static string RuntimeActorId(string sessionId, string scenarioId) =>
        $"scenario-flow-runtime/{ScenarioFlowRuntimePaths.SanitizeSegment(sessionId)}/{ScenarioFlowRuntimePaths.SanitizeSegment(scenarioId)}";
}
