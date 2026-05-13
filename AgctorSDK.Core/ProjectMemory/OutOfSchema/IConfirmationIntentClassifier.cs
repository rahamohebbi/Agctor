using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>
/// Resolves a user reply to <see cref="ConfirmationInputDetector.ConfirmationSignal"/> for the PRD-019 generic-inbox prompt.
/// Implementations may be deterministic, LLM-backed, or hybrid; an injected service lets us swap strategies
/// (heuristic in tests, LLM-aware in the Host) without changing pipeline call sites.
/// </summary>
public interface IConfirmationIntentClassifier
{
    /// <param name="userMessage">Raw user reply.</param>
    /// <param name="lastAssistantPromptText">Most recent assistant prompt to ground intent (e.g. the curator's yes/no question). May be null when unknown.</param>
    Task<ConfirmationInputDetector.ConfirmationSignal> ClassifyAsync(
        string? userMessage,
        string? lastAssistantPromptText,
        CancellationToken cancellationToken = default);
}
