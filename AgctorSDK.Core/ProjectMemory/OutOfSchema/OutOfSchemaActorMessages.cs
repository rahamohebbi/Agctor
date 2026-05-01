using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>
/// Serializable envelope for hosts or actors that materialize approved generic-inbox rows (PRD-019).
/// </summary>
public sealed record PersistApprovedGenericFactsCommand(
    string ProjectRoot,
    string? ScenarioId,
    IReadOnlyList<ApprovedGenericFact> Approvals);
