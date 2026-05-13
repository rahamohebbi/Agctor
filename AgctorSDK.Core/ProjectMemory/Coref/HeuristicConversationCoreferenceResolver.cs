using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>
/// No-op default for environments without an LLM (e.g. unit tests). Returns the message unchanged
/// but still surfaces the persisted focus so downstream prompts can use it as a hint.
/// </summary>
public sealed class HeuristicConversationCoreferenceResolver : IConversationCoreferenceResolver
{
    public Task<CoreferenceResolution> ResolveAsync(CoreferenceRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(CoreferenceResolution.Unchanged(
            request.UserMessage,
            request.CurrentFocus?.EntityKey,
            "heuristic-resolver"));
}
