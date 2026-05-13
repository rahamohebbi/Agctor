using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>
/// Persists the most-recently-named entity for a (project, scenario). Survives Host restarts and
/// new browser sessions so a follow-up like "He likes basketball" can still resolve to the right entity.
/// </summary>
public interface IConversationFocusStore
{
    /// <summary>Returns the current focus for a scenario or null when none has been recorded.</summary>
    Task<ConversationFocus?> LoadAsync(string projectRoot, string? scenarioId, CancellationToken cancellationToken = default);

    /// <summary>Writes the focus row, replacing any previous focus for the same scenario.</summary>
    Task SaveAsync(string projectRoot, string? scenarioId, ConversationFocus focus, CancellationToken cancellationToken = default);
}

/// <summary>One row of focus state used by coreference resolution and pipeline hints.</summary>
public sealed class ConversationFocus
{
    /// <summary>Canonical slug of the most recently named person (e.g. <c>raha</c>).</summary>
    public string EntityKey { get; set; } = "";

    /// <summary>Display name as last seen (e.g. <c>Raha Mohebbi</c>) — useful for prompts.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>UTC instant the focus was last updated.</summary>
    public string UpdatedAtUtc { get; set; } = "";

    /// <summary>Optional session id that produced this update, for diagnostics only.</summary>
    public string? UpdatedBySessionId { get; set; }

    /// <summary>How focus was set: <c>extracted</c> (intent named the person) or <c>resolved</c> (coref resolver).</summary>
    public string Source { get; set; } = "extracted";
}
