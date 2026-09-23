using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Completion
{
    /// <summary>
    /// A single text-completion request. System text is guidance; prompt is the task.
    /// Kept as a message payload so actors never call a model directly.
    /// </summary>
    public sealed record TextGenerationRequest(string Prompt, string? SystemPrompt = null);

    /// <summary>
    /// Outcome of one completion. Failure does not throw: callers decide how to fall back.
    /// </summary>
    public sealed record TextGenerationResult(bool Succeeded, string Text, string Source, string? Error = null);

    /// <summary>
    /// Pluggable text generator. Implementations must not throw for model or network failure.
    /// </summary>
    public interface ITextGenerator
    {
        /// <summary>Stable name recorded on <see cref="TextGenerationResult.Source"/>.</summary>
        string Name { get; }

        Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Message a <c>CompletionAgent</c> understands.
    /// When generation fails and <see cref="FallbackText"/> is set, the actor returns that text
    /// instead of faulting. That keeps product actors working without a local model.
    /// </summary>
    public sealed record CompletionRequest(string Prompt, string? SystemPrompt = null, string? FallbackText = null);
}
