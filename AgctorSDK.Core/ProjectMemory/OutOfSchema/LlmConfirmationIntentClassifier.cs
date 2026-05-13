using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using Microsoft.Extensions.Logging;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>
/// LLM-first classifier for PRD-019 confirmation turns. The heuristic phrases are reused as
/// few-shot examples so the LLM treats them consistently without code edits per phrasing variant.
/// Falls back to the deterministic heuristic only when the LLM call itself fails (network/Ollama down).
/// </summary>
public sealed class LlmConfirmationIntentClassifier : IConfirmationIntentClassifier
{
    private const int MaxUserCharsForLlm = 240;

    private readonly IProjectMemoryLlmClient _llm;
    private readonly ILogger<LlmConfirmationIntentClassifier>? _logger;

    public LlmConfirmationIntentClassifier(
        IProjectMemoryLlmClient llm,
        ILogger<LlmConfirmationIntentClassifier>? logger = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger;
    }

    public async Task<ConfirmationInputDetector.ConfirmationSignal> ClassifyAsync(
        string? userMessage,
        string? lastAssistantPromptText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return ConfirmationInputDetector.ConfirmationSignal.None;

        // Long messages should be treated as new content (e.g. "Raha also has two dogs"), not consent.
        if (userMessage.Length > MaxUserCharsForLlm)
            return ConfirmationInputDetector.ConfirmationSignal.None;

        // Without the prior prompt context the LLM cannot tell consent from generic acknowledgement
        // (e.g. a stray "yes" at the start of a fresh conversation should not approve anything).
        if (string.IsNullOrWhiteSpace(lastAssistantPromptText))
            return ConfirmationInputDetector.ConfirmationSignal.None;

        var prompt = BuildPrompt(userMessage.Trim(), lastAssistantPromptText.Trim());
        try
        {
            var raw = await _llm.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            return ParseLabel(raw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Confirmation intent LLM classification failed; falling back to heuristic.");
            return ConfirmationInputDetector.Classify(userMessage);
        }
    }

    /// <summary>Builds a few-shot prompt using <see cref="ConfirmationInputDetector"/> phrases as canonical examples.</summary>
    private static string BuildPrompt(string userReply, string lastPrompt)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You classify a user's short reply to a previous assistant question that asked whether to store");
        sb.AppendLine("an out-of-schema fact. Reply with EXACTLY one token: AFFIRMATIVE, NEGATIVE, or NONE.");
        sb.AppendLine("AFFIRMATIVE = the user agrees, consents, or wants to save/store/keep the fact.");
        sb.AppendLine("NEGATIVE = the user refuses, declines, or says do not save it.");
        sb.AppendLine("NONE = the user is providing new information, asking a question, or it is unclear.");
        sb.AppendLine();
        sb.AppendLine("Examples (use these to ground your decision; classify the new reply with the same intent):");
        sb.AppendLine("AFFIRMATIVE: yes");
        sb.AppendLine("AFFIRMATIVE: yes please");
        sb.AppendLine("AFFIRMATIVE: ok");
        sb.AppendLine("AFFIRMATIVE: store it");
        sb.AppendLine("AFFIRMATIVE: store this fact");
        sb.AppendLine("AFFIRMATIVE: I want to save");
        sb.AppendLine("AFFIRMATIVE: I consent");
        sb.AppendLine("AFFIRMATIVE: yes I consent");
        sb.AppendLine("AFFIRMATIVE: yes I wish to save it");
        sb.AppendLine("AFFIRMATIVE: please store this fact");
        sb.AppendLine("AFFIRMATIVE: sounds great, please go ahead and write that down for me");
        sb.AppendLine("NEGATIVE: no");
        sb.AppendLine("NEGATIVE: no thanks");
        sb.AppendLine("NEGATIVE: skip");
        sb.AppendLine("NEGATIVE: not now");
        sb.AppendLine("NEGATIVE: please do not save it");
        sb.AppendLine("NEGATIVE: I do not consent to save it");
        sb.AppendLine("NONE: yes and also add his dog named Fido and also his car");
        sb.AppendLine("NONE: who is Raha?");
        sb.AppendLine("NONE: my last name is Mohebbi");
        sb.AppendLine();
        sb.Append("Previous assistant prompt:\n\"").Append(lastPrompt).AppendLine("\"");
        sb.Append("User reply:\n\"").Append(userReply).AppendLine("\"");
        sb.Append("Answer with one token only:");
        return sb.ToString();
    }

    private static ConfirmationInputDetector.ConfirmationSignal ParseLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ConfirmationInputDetector.ConfirmationSignal.None;

        // Take the first non-empty token; the model sometimes adds explanation after it.
        var token = raw.Trim();
        var firstWhitespace = token.IndexOfAny(new[] { ' ', '\n', '\r', '\t', '.', ',', ':' });
        if (firstWhitespace > 0)
            token = token[..firstWhitespace];

        if (token.Equals("AFFIRMATIVE", StringComparison.OrdinalIgnoreCase))
            return ConfirmationInputDetector.ConfirmationSignal.Affirmative;
        if (token.Equals("NEGATIVE", StringComparison.OrdinalIgnoreCase))
            return ConfirmationInputDetector.ConfirmationSignal.Negative;
        return ConfirmationInputDetector.ConfirmationSignal.None;
    }
}
