using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>Fallback when no LLM is registered: keep current focus unless lexical match finds a clearer subject.</summary>
public sealed class HeuristicFocusSubjectResolver : IFocusSubjectResolver
{
    public Task<FocusSubjectResult> ResolveAsync(FocusSubjectRequest request, CancellationToken cancellationToken = default)
    {
        var current = FocusEntityPolicy.NormalizeSlugOrNull(request?.CurrentFocusEntityKey);
        var hit = FocusEntityPolicy.TryMatchPrimaryEntityKeyInMessage(request?.UserMessage, request?.KnownEntities);
        var slug = FocusEntityPolicy.NormalizeSlugOrNull(hit) ?? current;
        return Task.FromResult(new FocusSubjectResult
        {
            EntityKey = slug,
            DisplayName = slug,
            Reason = hit != null ? "heuristic-explicit-name" : "heuristic-current",
            ChangedFromCurrent = !string.IsNullOrWhiteSpace(slug)
                                 && !string.Equals(slug, current, StringComparison.OrdinalIgnoreCase)
        });
    }
}
