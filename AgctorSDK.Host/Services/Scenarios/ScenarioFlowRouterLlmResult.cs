namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>Outcome of the routing LLM step (parse + whitelist + optional filters).</summary>
public sealed class ScenarioFlowRouterLlmResult
{
    public bool Ok { get; init; }

    public string? Error { get; init; }

    /// <summary>Subset of candidate <see cref="ScenarioFlowRouterPersonaCandidate.PersonaId"/> in model order.</summary>
    public IReadOnlyList<string>? SelectedPersonaIds { get; init; }

    public bool NeedsClarification { get; init; }

    public string? ClarificationPrompt { get; init; }

    public static ScenarioFlowRouterLlmResult Success(IReadOnlyList<string> selected) =>
        new() { Ok = true, SelectedPersonaIds = selected };

    public static ScenarioFlowRouterLlmResult Fail(string error) =>
        new() { Ok = false, Error = error };

    public static ScenarioFlowRouterLlmResult Clarify(string? prompt) =>
        new() { Ok = true, NeedsClarification = true, ClarificationPrompt = prompt };
}
