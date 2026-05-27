using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>LLM picks who the current message is mainly about (allowed entity slugs only).</summary>
public interface IFocusSubjectResolver
{
    Task<FocusSubjectResult> ResolveAsync(FocusSubjectRequest request, CancellationToken cancellationToken = default);
}

public sealed class FocusSubjectRequest
{
    public string UserMessage { get; init; } = "";
    public string? ConversationPrefix { get; init; }
    public string? CurrentFocusEntityKey { get; init; }
    public IReadOnlyList<KnownEntity> KnownEntities { get; init; } = System.Array.Empty<KnownEntity>();
}

public sealed class FocusSubjectResult
{
    public string? EntityKey { get; init; }
    public string? DisplayName { get; init; }
    public string Reason { get; init; } = "";
    public bool ChangedFromCurrent { get; init; }

    public static FocusSubjectResult Unchanged(string? currentKey, string reason) => new()
    {
        EntityKey = currentKey,
        Reason = reason
    };
}
