using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>
/// Rewrites a user message so implicit references (pronouns in any language, bare predicates without a subject)
/// resolve to a known entity slug. Output is multi-language friendly because it leans on an LLM with a
/// constrained whitelist of allowed entity slugs.
/// </summary>
public interface IConversationCoreferenceResolver
{
    /// <summary>
    /// Returns either an unchanged message, or a rewritten message plus the active subject. Implementations
    /// must NEVER invent slugs outside <paramref name="knownEntities"/>; on ambiguity, return the original
    /// message and rely on the active subject hint downstream.
    /// </summary>
    Task<CoreferenceResolution> ResolveAsync(
        CoreferenceRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Inputs for a single coreference resolution call.</summary>
public sealed class CoreferenceRequest
{
    public string UserMessage { get; init; } = "";

    /// <summary>Optional prior conversation prefix (User/Assistant lines, newest at the end).</summary>
    public string? ConversationPrefix { get; init; }

    /// <summary>Most recently named entity for this scenario (from <see cref="IConversationFocusStore"/>).</summary>
    public ConversationFocus? CurrentFocus { get; init; }

    /// <summary>Allowed slugs the LLM may emit as <c>activeSubject</c> or use in the rewrite.</summary>
    public IReadOnlyList<KnownEntity> KnownEntities { get; init; } = System.Array.Empty<KnownEntity>();
}

/// <summary>Compact view of one discovered entity passed to the LLM as a hint.</summary>
public sealed class KnownEntity
{
    public string EntityKey { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public IReadOnlyList<string>? Aliases { get; init; }
}

/// <summary>Outcome of <see cref="IConversationCoreferenceResolver.ResolveAsync"/>.</summary>
public sealed class CoreferenceResolution
{
    public bool Changed { get; init; }
    public string RewrittenMessage { get; init; } = "";
    public string? ActiveSubjectEntityKey { get; init; }

    /// <summary>Why the resolver returned a particular result (used in pipeline trace).</summary>
    public string Reason { get; init; } = "";

    public static CoreferenceResolution Unchanged(string original, string? subject, string reason) => new()
    {
        Changed = false,
        RewrittenMessage = original,
        ActiveSubjectEntityKey = subject,
        Reason = reason
    };
}
