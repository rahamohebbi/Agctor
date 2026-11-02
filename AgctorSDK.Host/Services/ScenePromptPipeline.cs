using System.Text.Json;
using AgctorSDK.Core.SceneMemory;

namespace AgctorSDK.Host.Services;

public sealed class ScenePromptRuntimeOptions
{
    /// <summary>Soft sentence-boundary target; a single sentence is never cut to fit.</summary>
    public int PartCharacters { get; set; } = 350;
    /// <summary>Time allowed per model request, excluding this application's model queue.</summary>
    public double CallTimeoutSeconds { get; set; } = 180;
    /// <summary>Whole-preview budget, including queueing, smaller-section retries and corrections.</summary>
    public double TotalTimeoutSeconds { get; set; } = 900;
}

public sealed record ScenePromptProgress(string Stage, int Part, int TotalParts, int CompletedParts, string Message, int Attempt = 0);
internal sealed record ScenePromptPart(int Start, string Text, int Depth = 0);
internal sealed class ScenePromptSectionLimitException(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}
internal sealed class ScenePromptCallTimeoutException : TimeoutException;

internal sealed class ScenePromptCheckpoint
{
    internal string? Identity;
    internal List<ScenePromptPart>? Parts;
    internal SceneWorkspace? Staged;
    internal readonly List<ScenePromptChange> Changes = [];
    internal readonly List<ScenePromptEvent> Events = [];
    internal readonly List<string> Questions = [];
    internal string PreviousText = "";
    internal int NextPart;
    internal bool ContinuityChecked;
    internal static string Key(SceneWorkspace workspace, string prompt, string model) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { revision = ScenePromptReducer.Revision(workspace), prompt, model }))));
}

/// <summary>Sequential, in-memory interpretation. Only a complete, revalidated plan leaves this pipeline.</summary>
internal static class ScenePromptPipeline
{
    // Break only at paragraph/sentence boundaries outside double-quoted dialogue. Never cut a sentence to meet a size target.
    internal static List<ScenePromptPart> Split(string prompt, int target, int offset = 0, int depth = 0)
    {
        var boundaries = new List<int>(); var quoted = false;
        for (var i = 0; i < prompt.Length; i++)
        {
            var c = prompt[i];
            if (c == '“') quoted = true;
            else if (c == '”') quoted = false;
            else if (c == '"' && (i == 0 || prompt[i - 1] != '\\')) quoted = !quoted;
            if (quoted) continue;
            if (c == '\n' && i + 1 < prompt.Length && prompt[i + 1] == '\n') boundaries.Add(i + 2);
            else if ((c is '.' or '!' or '?') && i + 1 < prompt.Length && char.IsWhiteSpace(prompt[i + 1])) boundaries.Add(i + 1);
            else if ((c is '”' or '"') && i > 0 && (prompt[i - 1] is '.' or '!' or '?') && i + 1 < prompt.Length && char.IsWhiteSpace(prompt[i + 1])) boundaries.Add(i + 1);
        }
        boundaries.Add(prompt.Length);
        var parts = new List<ScenePromptPart>(); var start = 0;
        while (start < prompt.Length)
        {
            var available = boundaries.Where(b => b > start).ToArray();
            var end = available.Where(b => b - start <= target).DefaultIfEmpty(available[0]).Max();
            if (prompt.Length - end < 100) end = prompt.Length;
            var from = start; var to = end;
            while (from < to && char.IsWhiteSpace(prompt[from])) from++;
            while (to > from && char.IsWhiteSpace(prompt[to - 1])) to--;
            if (to > from) parts.Add(new(offset + from, prompt[from..to], depth));
            start = end;
        }
        return parts;
    }

    internal static async Task<ScenePromptExtraction> RunAsync(SceneWorkspace original, string prompt, string model,
        ScenePromptRuntimeOptions options, IProgress<ScenePromptProgress>? progress,
        Func<SceneWorkspace, ScenePromptPart, string, int, int, CancellationToken, Task<ScenePromptExtraction>> interpret,
        CancellationToken ct, ScenePromptCheckpoint? checkpoint = null,
        Func<SceneWorkspace, CancellationToken, Task<ScenePromptExtraction>>? checkContinuity = null)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Enter a scene prompt.");
        checkpoint ??= new();
        var identity = ScenePromptCheckpoint.Key(original, prompt, model);
        if (checkpoint.Identity != null && checkpoint.Identity != identity)
            throw new InvalidOperationException("This interpretation checkpoint belongs to a different scene, prompt or model.");
        checkpoint.Identity = identity;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TotalTimeoutSeconds, 1, 1800)));
        var parts = checkpoint.Parts ??= Split(prompt, Math.Clamp(options.PartCharacters, 250, 1200));
        var staged = checkpoint.Staged ?? original;
        var changes = checkpoint.Changes; var events = checkpoint.Events; var questions = checkpoint.Questions;
        var previousText = checkpoint.PreviousText; var index = checkpoint.NextPart;
        var checkingContinuity = false;
        if (index > 0) progress?.Report(new("resuming", Math.Min(index + 1, parts.Count), parts.Count, index,
            $"Resuming with {index} checked sections. Your saved scene is unchanged."));
        bool TrySplit(ScenePromptPart part, string reason)
        {
            var smaller = Split(part.Text, Math.Max(160, part.Text.Length / 2), part.Start, part.Depth + 1);
            if (part.Depth >= 2 || smaller.Count <= 1 || parts.Count + smaller.Count - 1 > 40) return false;
            parts.RemoveAt(index); parts.InsertRange(index, smaller);
            progress?.Report(new("splitting", index + 1, parts.Count, index,
                reason + " Trying smaller sections; completed sections are kept."));
            return true;
        }
        try
        {
            for (; index < parts.Count; index++)
            {
                budget.Token.ThrowIfCancellationRequested();
                var part = parts[index];
                progress?.Report(new("interpreting", index + 1, parts.Count, index, $"Interpreting section {index + 1} of {parts.Count}."));
                ScenePromptExtraction extraction;
                try { extraction = await interpret(staged, part, previousText, index + 1, parts.Count, budget.Token); }
                catch (ScenePromptCallTimeoutException)
                {
                    if (TrySplit(part, "This section is taking too long.")) { index--; continue; }
                    throw Failure(prompt, model, part, index, "section_timeout",
                        $"The selected model exceeded {Math.Clamp(options.CallTimeoutSeconds, 0.05, 600):0.##} seconds for this section, even at the available sentence boundaries. {index} sections completed; none were saved.");
                }
                catch (ScenePromptSectionLimitException ex)
                {
                    if (TrySplit(part, ex.Message)) { index--; continue; }
                    throw Failure(prompt, model, part, index, ex.Code, ex.Message);
                }
                catch (ScenePromptInterpretationException ex)
                {
                    if (TrySplit(part, "The model needs a smaller section to resolve its interpretation.")) { index--; continue; }
                    throw new ScenePromptInterpretationException(ex.Message, model, prompt, ex.Attempts.Select(a => a with {
                        Issues = a.Issues.Select(i => i with {
                            Path = $"parts[{index}].{i.Path}", SourceStart = i.SourceStart.HasValue ? part.Start + i.SourceStart : null
                        }).ToArray() }).ToArray());
                }
                budget.Token.ThrowIfCancellationRequested();
                if (parts.Count == 1) return extraction;
                var next = ScenePromptReducer.Apply(staged, new(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(staged),
                    part.Text, "", model, extraction), allowQuestions: true);
                var mapped = Canonicalize(staged, next, extraction);
                changes.AddRange(mapped.Changes); events.AddRange(mapped.Events ?? Array.Empty<ScenePromptEvent>()); questions.AddRange(mapped.Questions);
                staged = next; previousText = part.Text;
                checkpoint.Staged = staged; checkpoint.PreviousText = previousText; checkpoint.NextPart = index + 1;
                progress?.Report(new("section_complete", index + 1, parts.Count, index + 1, $"Section {index + 1} of {parts.Count} checked."));
            }
            if (checkContinuity != null && !checkpoint.ContinuityChecked)
            {
                checkingContinuity = true;
                progress?.Report(new("continuity", 0, 0, parts.Count,
                    "Checking the final scene against the whole story, including older relationships."));
                ScenePromptExtraction corrections;
                try { corrections = await checkContinuity(staged, budget.Token); }
                catch (ScenePromptCallTimeoutException)
                {
                    throw new ScenePromptInterpretationException("The final continuity check timed out.", model, prompt, [
                        new(1, [ScenePromptDiagnostics.Issue(prompt, "continuity_timeout", "continuity",
                            "All sections were interpreted, but the final continuity check ran out of time. No changes were saved.",
                            suggestion: "Retry the unchanged prompt to resume the continuity check.")])]);
                }
                catch (ScenePromptSectionLimitException ex)
                {
                    throw new ScenePromptInterpretationException(ex.Message, model, prompt, [
                        new(1, [ScenePromptDiagnostics.Issue(prompt, ex.Code, "continuity", ex.Message,
                            suggestion: "Retry the unchanged prompt to resume the continuity check.")])]);
                }
                catch (ScenePromptInterpretationException ex)
                {
                    throw new ScenePromptInterpretationException(ex.Message, model, prompt, ex.Attempts.Select(a => a with {
                        Issues = a.Issues.Select(i => i with { Path = "continuity." + i.Path }).ToArray()
                    }).ToArray());
                }
                budget.Token.ThrowIfCancellationRequested();
                var checkedState = ScenePromptReducer.Apply(staged, new(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(staged),
                    prompt, "", model, corrections), allowQuestions: true);
                // This pass may correct existing identities only; it cannot silently introduce a second cast.
                if (checkedState.Entities.Count != staged.Entities.Count || corrections.Events?.Count > 0)
                    throw new ArgumentException("The continuity check must only correct state on known entities.");
                var mapped = Canonicalize(staged, checkedState, corrections);
                changes.AddRange(mapped.Changes); questions.AddRange(mapped.Questions);
                checkpoint.Staged = staged = checkedState; checkpoint.ContinuityChecked = true;
            }
            progress?.Report(new("validating", parts.Count, parts.Count, parts.Count, "Checking the combined scene and continuity."));
            var known = original.Entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            var result = new ScenePromptExtraction(staged.Entities.Where(e => !known.Contains(e.Id)).Select(e => new ScenePromptEntity(e.Id, e.Name, e.Kind)).ToArray(),
                changes, questions.Distinct().ToArray(), events);
            ScenePromptReducer.Apply(original, new(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(original), prompt, "", model, result), allowQuestions: true);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (checkingContinuity)
                throw new ScenePromptInterpretationException("The final continuity check reached the preview time limit.", model, prompt, [
                    new(1, [ScenePromptDiagnostics.Issue(prompt, "continuity_timeout", "continuity",
                        "All sections were interpreted, but the overall time limit interrupted the final continuity check. No changes were saved.",
                        suggestion: "Retry the unchanged prompt to resume the continuity check.")])]);
            var part = parts[Math.Min(index, parts.Count - 1)];
            throw Failure(prompt, model, part, index, "preview_timeout", $"The preview reached its overall time limit after {Math.Min(index, parts.Count)} sections. No sections were saved.");
        }
    }

    private static ScenePromptInterpretationException Failure(string prompt, string model, ScenePromptPart part, int index, string code, string message) =>
        new(message, model, prompt, new[] { new ScenePromptAttempt(1, new[] {
            ScenePromptDiagnostics.Issue(prompt, code, $"parts[{index}]", message, evidence: part.Text,
                suggestion: code.EndsWith("timeout", StringComparison.Ordinal)
                    ? "This is a processing timeout, not evidence of an invalid sentence. Check that the local model is responsive and not processing another request; retry when ready."
                    : "The model could not fit a complete result within its response limit. Review the highlighted section or try another interpretation model.")
                with { SourceStart = part.Start, SourceLength = part.Text.Length } }) });

    private static ScenePromptExtraction Canonicalize(SceneWorkspace before, SceneWorkspace after, ScenePromptExtraction plan)
    {
        var originalIds = before.Entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var created = new Queue<SceneEntity>(after.Entities.Where(e => !originalIds.Contains(e.Id)));
        var refs = before.Entities.ToDictionary(e => e.Id, e => e.Id, StringComparer.Ordinal);
        foreach (var entity in plan.Entities)
        {
            if (refs.ContainsKey(entity.Ref)) continue;
            var existing = before.Entities.FirstOrDefault(e => string.Equals(e.Name, entity.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            refs[entity.Ref] = existing?.Id ?? created.Dequeue().Id;
        }
        string Resolve(string value) => refs.TryGetValue(value, out var id) ? id :
            after.Entities.FirstOrDefault(e => string.Equals(e.Name, value, StringComparison.OrdinalIgnoreCase))?.Id ?? value;
        return plan with {
            Changes = plan.Changes.Select(c => c with { Subject = Resolve(c.Subject), Target = Resolve(c.Target),
                Value = UsesReferenceValue(c.Property) ? Resolve(c.Value) : c.Value }).ToArray(),
            Events = plan.Events?.Select(e => e with { Participants = e.Participants.Select(Resolve).ToArray() }).ToArray()
        };
    }

    internal static bool UsesReferenceValue(string property) => property is "held_by" or "in" or "on" or "located_at" or "has" or "holds" or "holding" or "carries" or "carrying" or "owns" or "owned_by" or "consumed_by" or "destroyed_by";
}

/// <summary>Strip storage metadata and repeated source text; short IDs keep follow-ups economical.</summary>
internal sealed class ScenePromptContext(SceneWorkspace workspace)
{
    private readonly Dictionary<string, string> _aliases = workspace.Entities.Select((e, i) => (Alias: $"e{i + 1}", e.Id)).ToDictionary(e => e.Alias, e => e.Id);
    internal string Serialize(string prompt, string precedingSection, IReadOnlyDictionary<string, string>? evidenceSources = null)
    {
        var ids = _aliases.ToDictionary(e => e.Value, e => e.Key);
        string Alias(string id) => ids.GetValueOrDefault(id, id);
        var facts = workspace.History.Where(f => !f.IsEvent).GroupBy(f => (f.EntityId, f.Property)).Select(g => g.Last()).Where(f => !f.Removed)
            .Select(f => new { subject = Alias(f.EntityId), property = f.Property, value = f.RelatedEntityId == null || !ScenePromptPipeline.UsesReferenceValue(f.Property) ? f.Value : "", target = f.RelatedEntityId == null ? "" : Alias(f.RelatedEntityId) });
        return JsonSerializer.Serialize(new {
            entities = workspace.Entities.Select(e => new { id = Alias(e.Id), e.Name, e.Kind }), currentFacts = facts,
            studioReferences = workspace.Entities.Where(e => e.Studio != null).Select(e => new {
                entity = Alias(e.Id), displayName = e.Studio!.DisplayName, defaults = e.Studio.Defaults }),
            recentEvents = workspace.History.Where(f => f.IsEvent).TakeLast(4).Select(f => new { description = f.Value, mode = f.EventMode, participants = f.ParticipantIds?.Select(Alias) }),
            precedingSection, prompt, evidenceSources
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    internal ScenePromptExtraction Resolve(ScenePromptExtraction plan)
    {
        string Ref(string value) => value == null ? null! : _aliases.GetValueOrDefault(value, value);
        return plan with {
            Entities = plan.Entities?.Select(e => e == null ? e! : e with { Ref = Ref(e.Ref) }).ToArray()!,
            Changes = plan.Changes?.Select(c => c == null ? c! : c with { Subject = Ref(c.Subject), Target = Ref(c.Target),
                Value = ScenePromptPipeline.UsesReferenceValue(c.Property) ? Ref(c.Value) : c.Value }).ToArray()!,
            Events = plan.Events?.Select(e => e == null ? e! : e with { Participants = e.Participants?.Select(Ref).ToArray()! }).ToArray()
        };
    }
}

/// <summary>Model output cites small source IDs instead of repeating the same prose on every state change.</summary>
internal sealed class ScenePromptSources(string prompt)
{
    internal IReadOnlyDictionary<string, string> Entries { get; } = ScenePromptPipeline.Split(prompt, 180)
        .Select((part, i) => (Key: $"source_{i + 1}", part.Text)).ToDictionary(p => p.Key, p => p.Text);

    internal ScenePromptExtraction Restore(ScenePromptExtraction plan)
    {
        string Evidence(string value) => value == null ? null! : Entries.GetValueOrDefault(value, value);
        return plan with {
            Changes = plan.Changes?.Select(c => c == null ? c! : c with { Evidence = Evidence(c.Evidence) }).ToArray()!,
            Events = plan.Events?.Select(e => e == null ? e! : e with { Evidence = Evidence(e.Evidence) }).ToArray()
        };
    }
}

internal sealed class ScenePromptProgressReporter(Action<ScenePromptProgress> report) : IProgress<ScenePromptProgress>
{
    public void Report(ScenePromptProgress value) => report(value);
}
