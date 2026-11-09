using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenePromptPipelineTests
{
    private static SceneWorkspace Empty() => new("scene", "Scene", DateTimeOffset.UtcNow, [], []);
    private static ScenePromptExtraction Nothing() => new([], [], [], []);
    private static ScenePromptPreview Preview(SceneWorkspace w, string text, ScenePromptExtraction result) =>
        new("preview", ScenePromptReducer.Revision(w), text, "", "test", result);

    [Fact]
    public void Studio_descriptions_are_available_as_references_separate_from_current_facts()
    {
        var workspace = Empty() with {
            Entities = [new("maya", "Maya", "Character", new("maya-main", "character-42", 2, "Maya Explorer",
                new Dictionary<string, string> { ["clothing"] = "blue coat" }))],
            History = [new(1, "maya", "clothing", "green coat", "Maya changes coats", DateTimeOffset.UtcNow)]
        };
        using var context = JsonDocument.Parse(new ScenePromptContext(workspace).Serialize("Maya walks.", ""));
        var reference = context.RootElement.GetProperty("studioReferences")[0];
        Assert.Equal("e1", reference.GetProperty("entity").GetString());
        Assert.Equal("Maya Explorer", reference.GetProperty("displayName").GetString());
        Assert.Equal("blue coat", reference.GetProperty("defaults").GetProperty("clothing").GetString());
        Assert.Equal("green coat", context.RootElement.GetProperty("currentFacts")[0].GetProperty("value").GetString());
    }

    [Fact]
    public void Continuity_reviews_require_all_facts_and_address_actual_legacy_properties()
    {
        var workspace = Empty() with {
            Entities = [new("a", "Raha", "Character"), new("s", "Slime", "Object")],
            History = [new(1, "a", "trapped_side_by_side", "Ryan", "initial", DateTimeOffset.UtcNow),
                       new(2, "s", "sticks_feet_to", "Raha", "initial", DateTimeOffset.UtcNow, "a")]
        };
        var check = new ScenePromptContinuity(workspace);
        var result = check.Attach("""
            {"factReviews":{"fact_1":{"reason":"They leap free.","decision":"remove","evidence":"source_1"},
                            "fact_2":{"reason":"The restraint ends.","decision":"remove","evidence":"source_1"}}}
            """, Nothing());
        Assert.Equal(new[] { "trapped_side_by_side", "sticks_feet_to" }, result.Changes.Select(c => c.Property));
        Assert.All(result.Changes, c => Assert.True(c.Remove));
        Assert.Throws<ArgumentException>(() => check.Attach("""{"factReviews":{"fact_1":{"decision":"keep"}}}""", Nothing()));
        Assert.Throws<ArgumentException>(() => check.Attach("""
            {"factReviews":{"fact_1":{"decision":"keep"},"fact_2":{"decision":"keep"}}}
            """, new([], [new("a", "trapped_side_by_side", "", "", true, "source_1")], [])));
    }

    [Fact]
    public void Transport_schema_excludes_empty_changes_and_requires_a_known_source()
    {
        var schema = OllamaScenePromptInterpreter.BuildSchema(["source_1"]);
        var alternatives = schema["properties"]!["changes"]!["items"]!["oneOf"]!.AsArray();
        Assert.Equal(3, alternatives.Count);
        foreach (var branch in alternatives)
        {
            var fields = branch!["required"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
            var payload = Assert.Single(fields.Except(new[] { "subject", "property", "evidence" }));
            if (payload == "remove") Assert.True(branch["properties"]!["remove"]!["const"]!.GetValue<bool>());
            else Assert.Equal(1, branch["properties"]![payload]!["minLength"]!.GetValue<int>());
            Assert.Equal("source_1", branch["properties"]!["evidence"]!["enum"]![0]!.GetValue<string>());
            Assert.False(branch["additionalProperties"]!.GetValue<bool>());
        }
    }

    [Fact]
    public void Compact_wire_plan_restores_exact_source_and_only_defaults_omitted_fields()
    {
        const string prompt = "Raha says, “Now, Ryan!”";
        var sources = new ScenePromptSources(prompt);
        var plan = OllamaScenePromptInterpreter.DeserializeExtraction("""
            {"entities":[],"changes":[{"subject":"raha","property":"speech","value":"Now, Ryan!","evidence":"source_1"}],
             "events":[{"description":"Raha calls to Ryan","participants":["raha","ryan"],"evidence":"source_1","mode":"occurred"}],"questions":[]}
            """);
        var restored = sources.Restore(plan);
        Assert.Equal(prompt, restored.Changes[0].Evidence);
        Assert.Equal(prompt, restored.Events![0].Evidence);
        Assert.Equal("", restored.Changes[0].Target);
        Assert.False(restored.Changes[0].Remove);
        var invalid = OllamaScenePromptInterpreter.DeserializeExtraction("""
            {"entities":[],"changes":[{"subject":"a","property":"color","value":null,"evidence":"missing_source"}],"questions":[]}
            """);
        Assert.Null(invalid.Changes[0].Value);
        Assert.Equal("missing_source", sources.Restore(invalid).Changes[0].Evidence);
    }

    [Fact]
    public void Split_preserves_exact_source_offsets_and_keeps_dialogue_and_indivisible_sentences_intact()
    {
        var quote = "Ryan says, “Wait. Don't go! Stay here.”";
        var longSentence = new string('x', 500) + ".";
        var text = "First scene.\n\n" + quote + "\n\n" + longSentence + "\n\nLast scene.";
        var parts = ScenePromptPipeline.Split(text, 80);
        Assert.Contains(parts, p => p.Text.Contains(quote));
        Assert.Contains(parts, p => p.Text.Contains(longSentence));
        Assert.All(parts, p => Assert.Equal(p.Text, text.Substring(p.Start, p.Text.Length)));
        Assert.Equal(string.Concat(text.Where(c => !char.IsWhiteSpace(c))),
            string.Concat(parts.SelectMany(p => p.Text).Where(c => !char.IsWhiteSpace(c))));
    }

    [Fact]
    public void Compact_context_keeps_current_state_and_resolves_aliases_without_history_metadata()
    {
        var w = Empty() with {
            Entities = [new("character-long-id", "Maya", "Character"), new("object-long-id", "Mug", "Object")],
            History = [
                new(1, "object-long-id", "color", "red", "old source must not repeat", DateTimeOffset.UtcNow),
                new(2, "object-long-id", "color", "blue", "new source must not repeat", DateTimeOffset.UtcNow),
                new(3, "object-long-id", "held_by", "Maya", "another source", DateTimeOffset.UtcNow, "character-long-id")]
        };
        var context = new ScenePromptContext(w);
        var json = context.Serialize("She drops it.", "Maya holds the mug.");
        Assert.Contains("blue", json);
        Assert.DoesNotContain("red", json);
        Assert.DoesNotContain("source", json);
        Assert.DoesNotContain("long-id", json);
        var result = context.Resolve(new([], [new("e2", "held_by", "e1", "", false, "She")], [],
            [new("She drops it", ["e1", "e2"], "She drops it")]));
        Assert.Equal("object-long-id", result.Changes[0].Subject);
        Assert.Equal("character-long-id", result.Changes[0].Value);
        Assert.Equal(new[] { "character-long-id", "object-long-id" }, result.Events![0].Participants);
    }

    [Fact]
    public async Task Sections_share_proposed_state_and_combine_new_refs_before_one_atomic_preview()
    {
        var first = "Maya has a mug. " + new string('a', 230) + ".";
        var second = "She drops it. " + new string('b', 230) + ".";
        var prompt = first + "\n\n" + second;
        var original = Empty();
        var calls = 0;
        var result = await ScenePromptPipeline.RunAsync(original, prompt, "test", new() { PartCharacters = 250 }, null,
            (state, part, previous, number, total, ct) => {
                calls++;
                if (number == 1) return Task.FromResult(new ScenePromptExtraction(
                    [new("maya", "Maya", "Character"), new("mug", "Mug", "Object")],
                    [new("mug", "held_by", "", "maya", false, "Maya has a mug")], []));
                Assert.Equal(first, previous);
                Assert.Equal(2, state.Entities.Count);
                var mug = state.Entities.Single(e => e.Name == "Mug");
                Assert.Contains(state.History, f => f.EntityId == mug.Id && f.Property == "held_by");
                return Task.FromResult(new ScenePromptExtraction([], [new(mug.Id, "held_by", "", "", true, "She drops it")], []));
            }, default);
        Assert.Equal(2, calls);
        Assert.Equal(2, result.Entities.Count);
        Assert.Equal(result.Changes[0].Subject, result.Changes[1].Subject);
        var proposed = ScenePromptReducer.Apply(original, Preview(original, prompt, result));
        Assert.True(proposed.History.Last(f => f.Property == "held_by").Removed);
        Assert.Single(proposed.Prompts!);
        Assert.Empty(original.Entities);
        Assert.Empty(original.History);
    }

    [Fact]
    public async Task Timed_out_section_splits_without_repeating_completed_sections()
    {
        var sentence = new string('a', 120) + ".";
        var prompt = string.Join("\n\n", Enumerable.Range(0, 6).Select(i => i + sentence));
        var firstCalls = 0; var timedOut = false; var updates = new List<ScenePromptProgress>();
        var original = Empty();
        await ScenePromptPipeline.RunAsync(original, prompt, "test", new() { PartCharacters = 300 },
            new ScenePromptProgressReporter(updates.Add), (state, part, previous, number, total, ct) => {
                if (part.Start == 0) firstCalls++;
                if (number == 2 && !timedOut) { timedOut = true; throw new ScenePromptCallTimeoutException(); }
                return Task.FromResult(Nothing());
            }, default);
        Assert.Equal(1, firstCalls);
        Assert.Contains(updates, p => p.Stage == "splitting" && p.CompletedParts == 1);
        Assert.Equal(4, updates.Last().TotalParts);
        Assert.Empty(original.History);
    }

    [Fact]
    public async Task Identical_named_props_stay_distinct_when_later_sections_change_only_one()
    {
        var first = "Raha and Ryan hold matching swords. " + new string('x', 210) + ".";
        var second = "Raha drops his sword. " + new string('y', 225) + ".";
        var prompt = first + "\n\n" + second;
        var original = Empty();
        var result = await ScenePromptPipeline.RunAsync(original, prompt, "test", new() { PartCharacters = 250 }, null,
            (state, part, previous, number, total, ct) => {
                if (number == 1) return Task.FromResult(new ScenePromptExtraction(
                    [new("a", "Raha", "Character"), new("b", "Ryan", "Character"),
                     new("sa", "Glowing sword", "Object"), new("sb", "Glowing sword", "Object")],
                    [new("sa", "held_by", "", "a", false, "Raha and Ryan hold matching swords"),
                     new("sb", "held_by", "", "b", false, "Raha and Ryan hold matching swords")], []));
                var raha = state.Entities.Single(e => e.Name == "Raha");
                var sword = state.History.Single(f => f.Property == "held_by" && f.RelatedEntityId == raha.Id).EntityId;
                return Task.FromResult(new ScenePromptExtraction([], [new(sword, "held_by", "", "", true, "Raha drops his sword")], []));
            }, default);
        var proposed = ScenePromptReducer.Apply(original, Preview(original, prompt, result));
        Assert.Equal(2, proposed.Entities.Count(e => e.Kind == "Object"));
        var remaining = Assert.Single(proposed.History.Where(f => f.Property == "held_by")
            .GroupBy(f => f.EntityId).Select(g => g.Last()).Where(f => !f.Removed));
        Assert.Equal(proposed.Entities.Single(e => e.Name == "Ryan").Id, remaining.RelatedEntityId);
        Assert.Empty(original.Entities);
    }

    [Fact]
    public async Task Failure_in_later_section_reports_original_prompt_offset_not_local_offset()
    {
        var first = new string('a', 249) + ".";
        var second = new string('b', 249) + ".";
        var prompt = first + "\n\n" + second;
        var error = await Assert.ThrowsAsync<ScenePromptInterpretationException>(() =>
            ScenePromptPipeline.RunAsync(Empty(), prompt, "test", new() { PartCharacters = 250 }, null,
                (state, part, previous, number, total, ct) => {
                    if (number == 1) return Task.FromResult(Nothing());
                    throw new ScenePromptInterpretationException("Invalid", "test", part.Text, [
                        new(1, [ScenePromptDiagnostics.Issue(part.Text, "test", "changes[0]", "Invalid", evidence: part.Text)])]);
                }, default));
        var issue = Assert.Single(error.Attempts[0].Issues);
        Assert.Equal("parts[1].changes[0]", issue.Path);
        Assert.Equal(first.Length + 2, issue.SourceStart);
        Assert.Equal(second, prompt.Substring(issue.SourceStart!.Value, issue.SourceLength!.Value));
    }

    [Fact]
    public async Task Unsplittable_timeout_identifies_section_without_claiming_bad_prompt()
    {
        const string prompt = "One indivisible sentence.";
        var error = await Assert.ThrowsAsync<ScenePromptInterpretationException>(() =>
            ScenePromptPipeline.RunAsync(Empty(), prompt, "test", new(), null,
                (state, part, previous, number, total, ct) => throw new ScenePromptCallTimeoutException(), default));
        var issue = Assert.Single(error.Attempts[0].Issues);
        Assert.Equal("section_timeout", issue.Code);
        Assert.Equal(prompt, issue.Evidence);
        Assert.Contains("not evidence of an invalid sentence", issue.Suggestion);
    }

    [Fact]
    public async Task Retry_resumes_validated_sections_and_rejects_changed_context()
    {
        var prompt = new string('a', 249) + ".\n\n" + new string('b', 249) + ".";
        var checkpoint = new ScenePromptCheckpoint();
        var original = Empty();
        var firstCalls = 0; var fail = true;
        Task<ScenePromptExtraction> Interpret(SceneWorkspace state, ScenePromptPart part, string previous, int number, int total, CancellationToken ct)
        {
            if (number == 1) firstCalls++;
            if (number == 2 && fail) throw new ScenePromptCallTimeoutException();
            return Task.FromResult(Nothing());
        }
        await Assert.ThrowsAsync<ScenePromptInterpretationException>(() =>
            ScenePromptPipeline.RunAsync(original, prompt, "test", new() { PartCharacters = 250 }, null, Interpret, default, checkpoint));
        Assert.Equal(1, checkpoint.NextPart);
        fail = false;
        var updates = new List<ScenePromptProgress>();
        await ScenePromptPipeline.RunAsync(original, prompt, "test", new(), new ScenePromptProgressReporter(updates.Add), Interpret, default, checkpoint);
        Assert.Equal(1, firstCalls);
        Assert.Contains(updates, p => p.Stage == "resuming" && p.CompletedParts == 1);
        Assert.Empty(original.History);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScenePromptPipeline.RunAsync(original, prompt + " Changed.", "test", new(), null, Interpret, default, checkpoint));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScenePromptPipeline.RunAsync(original, prompt, "another-model", new(), null, Interpret, default, checkpoint));
    }

    [Fact]
    public async Task Cancellation_stops_work_without_returning_a_partial_preview()
    {
        using var stop = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ScenePromptPipeline.RunAsync(Empty(), new string('a', 250) + ".\n\n" + new string('b', 250) + ".",
                "test", new() { PartCharacters = 300 }, null, (state, part, previous, number, total, ct) => {
                    calls++; stop.Cancel(); ct.ThrowIfCancellationRequested();
                    return Task.FromResult(Nothing());
                }, stop.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Final_continuity_check_clears_incoming_stale_relations_and_resumes_without_reextracting()
    {
        var original = Empty() with {
            Entities = [new("maya", "Maya", "Character"), new("slime", "Slime", "Object")],
            History = [new(1, "maya", "stuck_in", "Slime", "initial", DateTimeOffset.UtcNow, "slime"),
                       new(2, "slime", "restrains", "Maya", "initial", DateTimeOffset.UtcNow, "maya")]
        };
        var prompt = "Maya leaps free. " + new string('a', 230) + ".\n\n" + new string('b', 249) + ".";
        var checkpoint = new ScenePromptCheckpoint();
        var sectionCalls = 0; var checks = 0;
        Task<ScenePromptExtraction> Interpret(SceneWorkspace state, ScenePromptPart part, string previous, int number, int total, CancellationToken ct)
        {
            sectionCalls++;
            return Task.FromResult(number == 1 ? new ScenePromptExtraction([],
                [new("maya", "stuck_in", "", "", true, "Maya leaps free")], []) : Nothing());
        }
        Task<ScenePromptExtraction> Check(SceneWorkspace state, CancellationToken ct)
        {
            Assert.True(state.History.Last(f => f.Property == "stuck_in").Removed);
            Assert.False(state.History.Last(f => f.Property == "restrains").Removed);
            if (++checks == 1) throw new ScenePromptCallTimeoutException();
            return Task.FromResult(new ScenePromptExtraction([], [new("slime", "restrains", "", "", true, "Maya leaps free")], []));
        }
        var error = await Assert.ThrowsAsync<ScenePromptInterpretationException>(() =>
            ScenePromptPipeline.RunAsync(original, prompt, "test", new() { PartCharacters = 250 }, null, Interpret, default, checkpoint, Check));
        Assert.Equal("continuity", error.Attempts[0].Issues[0].Path);
        var result = await ScenePromptPipeline.RunAsync(original, prompt, "test", new(), null, Interpret, default, checkpoint, Check);
        Assert.Equal(2, sectionCalls);
        Assert.Equal(2, checks);
        var proposed = ScenePromptReducer.Apply(original, Preview(original, prompt, result));
        Assert.True(proposed.History.Last(f => f.Property == "restrains").Removed);
        Assert.Equal(2, original.History.Count);
    }

    [Fact]
    public async Task Interpreter_deadline_cancels_transport_and_reports_processing_failure()
    {
        var factory = new Mock<IHttpClientFactory>();
        var handler = new SlowHandler();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(() => new HttpClient(handler, false));
        var interpreter = new OllamaScenePromptInterpreter(factory.Object, settings:
            Options.Create(new ScenePromptRuntimeOptions { CallTimeoutSeconds = 0.05 }));
        var error = await Assert.ThrowsAsync<ScenePromptInterpretationException>(() =>
            interpreter.InterpretAsync(Empty(), "Maya waves.", "test", default));
        Assert.Equal("section_timeout", error.Attempts[0].Issues[0].Code);
        Assert.True(handler.Cancelled);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        public bool Cancelled { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return new(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Local_model_calls_are_serialized_and_a_waiting_preview_can_be_cancelled()
    {
        var handler = new QueuedHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName))
            .Returns(() => new HttpClient(handler, false));
        var interpreter = new OllamaScenePromptInterpreter(factory.Object);
        var first = interpreter.InterpretAsync(Empty(), "Maya waves.", "test", default);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var stop = new CancellationTokenSource();
        var waiting = interpreter.InterpretAsync(Empty(), "Ryan waves.", "test", stop.Token);
        Assert.Equal(1, handler.Calls);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        handler.Release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        await interpreter.InterpretAsync(Empty(), "Raha waves.", "test", default);
        Assert.Equal(4, handler.Calls);
    }

    private sealed class QueuedHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new {
                content = "{\"entities\":[],\"changes\":[],\"events\":[],\"questions\":[]}" } }) };
        }
    }

    [Fact]
    public async Task Whole_preview_deadline_includes_waiting_and_has_source_diagnostics()
    {
        var error = await Assert.ThrowsAsync<ScenePromptInterpretationException>(() =>
            ScenePromptPipeline.RunAsync(Empty(), "Maya waves.", "test", new() { TotalTimeoutSeconds = 1 }, null,
                async (state, part, previous, number, total, ct) => {
                    await Task.Delay(Timeout.Infinite, ct);
                    return Nothing();
                }, default));
        Assert.Equal("preview_timeout", error.Attempts[0].Issues[0].Code);
        Assert.Equal("Maya waves.", error.Attempts[0].Issues[0].Evidence);
    }

    [Fact]
    public async Task Response_limit_triggers_section_split_instead_of_accepting_partial_json()
    {
        var text = string.Join(" ", Enumerable.Range(0, 4).Select(i => i + new string('x', 110) + "."));
        var calls = 0;
        var progress = new List<ScenePromptProgress>();
        var result = await ScenePromptPipeline.RunAsync(Empty(), text, "test", new(),
            new ScenePromptProgressReporter(progress.Add), (state, part, previous, number, total, ct) => {
                if (++calls == 1) throw new ScenePromptSectionLimitException("output_truncated", "Response too large.");
                return Task.FromResult(Nothing());
            }, default);
        Assert.InRange(calls, 3, 5);
        Assert.Contains(progress, p => p.Stage == "splitting");
        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task Cancelling_during_successful_call_still_discards_the_result()
    {
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ScenePromptPipeline.RunAsync(Empty(), "Maya waves.", "test", new(), null,
                (state, part, previous, number, total, ct) => {
                    stop.Cancel();
                    return Task.FromResult(Nothing());
                }, stop.Token));
    }
}
