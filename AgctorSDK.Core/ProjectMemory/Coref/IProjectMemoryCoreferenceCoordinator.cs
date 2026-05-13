using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>
/// Shared coreference preprocessing helper used by both the in-process pipeline runner and
/// the dashboard playground SSE flow path. Centralizes load-focus → discover-entities → resolve →
/// persist-focus so the two paths can never diverge on pronoun handling.
/// </summary>
public interface IProjectMemoryCoreferenceCoordinator
{
    /// <summary>
    /// Loads the persistent focus, discovers the known entities for the scenario, runs the resolver,
    /// and returns the (possibly rewritten) user message plus the active subject hint. Always succeeds:
    /// on any error the original message is returned with no active subject so the extractor still runs.
    /// </summary>
    Task<CoreferencePreprocessResult> PreprocessAsync(
        string projectRoot,
        string? scenarioId,
        string userMessage,
        string? conversationPrefix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the active subject after a successful extractor turn so the next turn (and a brand-new
    /// browser session in the same scenario) inherits it. Best-effort: failures are swallowed.
    /// </summary>
    Task PersistFocusFromExtractAsync(
        string projectRoot,
        string? scenarioId,
        string? rawExtractorLlmText,
        string? activeSubjectFromPreprocess,
        IReadOnlyList<KnownEntity> knownEntities,
        string? sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Output of <see cref="IProjectMemoryCoreferenceCoordinator.PreprocessAsync"/>.</summary>
public sealed class CoreferencePreprocessResult
{
    /// <summary>Message the extractor should consume (either the original or the resolver's rewrite).</summary>
    public string ResolvedUserMessage { get; init; } = "";

    /// <summary>True when the resolver actually rewrote the message (e.g. "He" → "Raha").</summary>
    public bool Changed { get; init; }

    /// <summary>Active subject slug for downstream hinting (focus-before + resolver fallback).</summary>
    public string? ActiveSubjectKey { get; init; }

    /// <summary>Display name matching <see cref="ActiveSubjectKey"/> when known.</summary>
    public string? ActiveSubjectDisplay { get; init; }

    /// <summary>Reason string for trace breadcrumbs (e.g. <c>llm-rewrite</c>, <c>no-context</c>).</summary>
    public string Reason { get; init; } = "";

    /// <summary>Focus row that was on disk before this turn (null when fresh).</summary>
    public ConversationFocus? FocusBefore { get; init; }

    /// <summary>Entities discovered under the scenario workspace — passed to the resolver as the whitelist.</summary>
    public IReadOnlyList<KnownEntity> KnownEntities { get; init; } = System.Array.Empty<KnownEntity>();
}
