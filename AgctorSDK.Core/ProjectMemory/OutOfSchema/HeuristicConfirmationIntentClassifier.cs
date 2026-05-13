using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>Deterministic classifier: pure pass-through to <see cref="ConfirmationInputDetector"/>.</summary>
public sealed class HeuristicConfirmationIntentClassifier : IConfirmationIntentClassifier
{
    public Task<ConfirmationInputDetector.ConfirmationSignal> ClassifyAsync(
        string? userMessage,
        string? lastAssistantPromptText,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConfirmationInputDetector.Classify(userMessage));
}
