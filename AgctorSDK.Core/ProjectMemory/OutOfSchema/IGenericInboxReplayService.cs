using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>
/// Back-fills entity files from <c>.agctor/runtime/generic-inbox/confirmed.yaml</c> by re-routing each
/// confirmed row through the current <c>routing-rules.yaml</c>. Idempotent via
/// <see cref="GenericInboxConfirmedRow.ReplayedAtUtc"/>: a successfully projected row is stamped and skipped on the next replay
/// unless <see cref="GenericInboxReplayOptions.IncludeAlreadyReplayed"/> is true.
/// </summary>
public interface IGenericInboxReplayService
{
    /// <summary>
    /// Re-routes confirmed rows through the freshest project schema and applies routed intents to entity files.
    /// </summary>
    /// <param name="projectRoot">Absolute path to the project root containing <c>.agctor</c>.</param>
    /// <param name="scenarioId">When set, only rows tagged with the matching <see cref="GenericInboxConfirmedRow.ScenarioSegment"/> are processed.</param>
    Task<GenericInboxReplayReport> ReplayAsync(
        string projectRoot,
        string? scenarioId = null,
        GenericInboxReplayOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Caller knobs for the replay (filters + force flag).</summary>
public sealed class GenericInboxReplayOptions
{
    /// <summary>When true, includes rows that already have <see cref="GenericInboxConfirmedRow.ReplayedAtUtc"/> set.</summary>
    public bool IncludeAlreadyReplayed { get; init; }

    /// <summary>Optional whitelist of <see cref="GenericInboxConfirmedRow.EntityKey"/> values.</summary>
    public IReadOnlyList<string>? OnlyEntityKeys { get; init; }

    /// <summary>Optional whitelist of <see cref="GenericInboxConfirmedRow.KnowledgeType"/> values.</summary>
    public IReadOnlyList<string>? OnlyKnowledgeTypes { get; init; }
}

/// <summary>Outcome of <see cref="IGenericInboxReplayService.ReplayAsync"/>.</summary>
public sealed class GenericInboxReplayReport
{
    /// <summary>Total rows that passed the scenario / filter gate (before routing).</summary>
    public int Considered { get; init; }

    /// <summary>Rows that the current routing rules matched.</summary>
    public int Routed { get; init; }

    /// <summary>Rows skipped because they were already replayed (and force flag is off).</summary>
    public int SkippedAlreadyReplayed { get; init; }

    /// <summary>Rows skipped because no routing rule matches yet (still out-of-schema).</summary>
    public int SkippedRouteMiss { get; init; }

    /// <summary>Rows that routed but no entity record could be resolved or bootstrapped.</summary>
    public int SkippedUnresolvedEntity { get; init; }

    /// <summary>Files projected to disk (deduplicated, project-relative or absolute as projection emits).</summary>
    public IReadOnlyList<string> UpdatedFiles { get; init; } = System.Array.Empty<string>();

    /// <summary>Per-row issues (route_miss, unresolved-entity, projection-error) for diagnostics.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = System.Array.Empty<ValidationIssue>();
}
