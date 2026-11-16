using AgctorSDK.Host.Services;
namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>Safe durable diagnostics; never publish model prose or provider exception bodies.</summary>
public sealed record ScenePlanProblem(int ClipNumber, string Stage, IReadOnlyList<string> Issues)
{
    public static ScenePlanProblem From(int index, ScenePromptInterpretationException error)
    {
        var issues = error.Attempts.LastOrDefault()?.Issues ?? [];
        return new(index + 1, issues.Any(i => i.Path.StartsWith("continuity", StringComparison.Ordinal)) ? "Final continuity check" : "Scene interpretation",
            issues.Select(i => i.Code switch {
                "invalid_participants" or "unknown_subject" => "An action refers to a character or object the plan could not identify.",
                "invalid_entity" => "The plan contains conflicting character or object identities.",
                "evidence_mismatch" or "missing_evidence" => "A planned action could not be grounded in this shot’s instructions.",
                "invalid_change" => "A planned relationship or state change is inconsistent.",
                "invalid_json" or "output_truncated" => "The model returned an incomplete plan.",
                "section_timeout" or "continuity_timeout" or "preview_timeout" or "queue_timeout" => "The scene check exceeded its time limit.",
                _ => "The model could not produce a consistent scene plan."
            }).Distinct().Take(4).ToArray());
    }
}
