using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>Deterministic routing hints when the playground turn includes photos (023e pre-router + LLM appendix).</summary>
public static class PlaygroundFlowAttachmentRouting
{
    public const string PersonExtractorPersonaId = PlaygroundFlowPreRouter.PersonExtractorPersonaId;

    /// <summary>Skip LLM router when pre-router picks a persona for this photo turn.</summary>
    public static bool TryPickPersona(
        PlaygroundFlowRoutingContext ctx,
        string? userMessage,
        IReadOnlyList<ScenarioFlowRouterPersonaCandidate> candidates,
        out string? personaId) =>
        PlaygroundFlowPreRouter.TryPickPersona(ctx, userMessage, candidates, out personaId);

    public static string BuildRoutingAppendix(PlaygroundFlowRoutingContext ctx)
    {
        if (!ctx.HasAttachments)
            return "";

        var lines = new List<string>
        {
            "[Playground routing context]",
            ctx.ToRouterText(),
            "",
            "Prefer style-coach for outfit/fashion photos, fitness-coach for gym/progress photos,",
            "person-extractor when the user wants to save facts, person-query for questions,",
            "relationship-coach for relationship advice, visual-intake when subject is unclear."
        };
        return string.Join("\n", lines);
    }

    public static bool IsBareConfirmation(string? userMessage) =>
        PlaygroundFlowPreRouter.IsBareConfirmation(userMessage);
}
