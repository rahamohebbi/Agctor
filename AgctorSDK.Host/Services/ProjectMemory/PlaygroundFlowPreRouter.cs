using System.Text.RegularExpressions;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>Deterministic persona pick before LLM Router when the turn includes photos (PRD-023 §11.1).</summary>
public static class PlaygroundFlowPreRouter
{
    private static readonly Regex BareYesNo = new(
        @"^\s*(yes|no|y|n)\s*\.?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SaveIntent = new(
        @"(?i)\b(save|persist|remember|store|keep|write|this is me|this is)\b");

    private static readonly Regex QuestionIntent = new(
        @"(?i)\b(who|what|when|where|how old|how's|how is|tell me about|do we know)\b|\?");

    private static readonly Regex StyleIntent = new(
        @"(?i)\b(outfit|fashion|style|wear|wearing|dress|look good|what should i wear|clothes|wardrobe)\b");

    private static readonly Regex FitnessIntent = new(
        @"(?i)\b(gym|workout|fitness|leg day|progress|form|exercise|training|muscle|weight loss|gains)\b");

    private static readonly Regex CoachIntent = new(
        @"(?i)\b(gift|reconnect|argument|relationship|friendship|support|advice|coach)\b");

    public const string StyleCoachPersonaId = "style-coach";
    public const string FitnessCoachPersonaId = "fitness-coach";
    public const string PersonExtractorPersonaId = "person-extractor";
    public const string PersonQueryPersonaId = "person-query";
    public const string RelationshipCoachPersonaId = "relationship-coach";
    public const string VisualIntakePersonaId = "visual-intake";

    /// <summary>First matching candidate wins; returns null to fall through to LLM Router.</summary>
    public static bool TryPickPersona(
        PlaygroundFlowRoutingContext ctx,
        string? userMessage,
        IReadOnlyList<ScenarioFlowRouterPersonaCandidate> candidates,
        out string? personaId)
    {
        personaId = null;
        if (!ctx.HasAttachments || candidates.Count == 0)
            return false;

        if (IsBareConfirmation(userMessage))
            return false;

        var text = userMessage?.Trim() ?? "";
        // User message wins over ctx snapshot (caption may differ from routed text after coref rewrite).
        var intent = InferSuggestedIntent(text);
        if (intent == "general" && !string.IsNullOrWhiteSpace(ctx.SuggestedIntent))
            intent = ctx.SuggestedIntent;

        if (intent == "style" && TryFind(candidates, StyleCoachPersonaId, out personaId))
            return true;

        if (intent == "fitness" && TryFind(candidates, FitnessCoachPersonaId, out personaId))
            return true;

        if (SaveIntent.IsMatch(text) && TryFind(candidates, PersonExtractorPersonaId, out personaId))
            return true;

        if (QuestionIntent.IsMatch(text) && TryFind(candidates, PersonQueryPersonaId, out personaId))
            return true;

        if (CoachIntent.IsMatch(text) && TryFind(candidates, RelationshipCoachPersonaId, out personaId))
            return true;

        // Photo-only or vague caption: prefer query/coach over silent extract.
        if (string.IsNullOrWhiteSpace(text) || text.Length < 24)
        {
            if (TryFind(candidates, PersonQueryPersonaId, out personaId))
                return true;
            if (TryFind(candidates, RelationshipCoachPersonaId, out personaId))
                return true;
        }

        if (TryFind(candidates, PersonExtractorPersonaId, out personaId))
            return true;

        return false;
    }

    public static string InferSuggestedIntent(string? userMessage)
    {
        var text = userMessage?.Trim() ?? "";
        if (StyleIntent.IsMatch(text))
            return "style";
        if (FitnessIntent.IsMatch(text))
            return "fitness";
        if (CoachIntent.IsMatch(text))
            return "relationship";
        if (SaveIntent.IsMatch(text))
            return "save";
        if (QuestionIntent.IsMatch(text))
            return "query";
        return "general";
    }

    public static bool IsBareConfirmation(string? userMessage) =>
        !string.IsNullOrWhiteSpace(userMessage) && BareYesNo.IsMatch(userMessage.Trim());

    private static bool TryFind(
        IReadOnlyList<ScenarioFlowRouterPersonaCandidate> candidates,
        string personaId,
        out string? picked)
    {
        picked = candidates
            .FirstOrDefault(c => string.Equals(c.PersonaId, personaId, StringComparison.OrdinalIgnoreCase))
            ?.PersonaId;
        return !string.IsNullOrWhiteSpace(picked);
    }
}
