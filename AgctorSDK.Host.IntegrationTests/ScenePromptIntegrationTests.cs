using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

[Collection("LlmStatic")]
public sealed class ScenePromptIntegrationTests : IClassFixture<ScenePromptWebApplicationFactory>
{
    private readonly HttpClient _client;
    public ScenePromptIntegrationTests(ScenePromptWebApplicationFactory factory) => _client = factory.CreateClient();

    private async Task<string> CreateWorkspace()
    {
        var response = await _client.PostAsJsonAsync("/api/scene-memory/workspaces", new { name = "Prompt test" });
        response.EnsureSuccessStatusCode();
        return "/api/scene-memory/workspaces/" + (await response.Content.ReadFromJsonAsync<SceneWorkspace>())!.Id;
    }

    private async Task<ScenePromptReview> Preview(string path, string prompt = "Maya holds a mug.")
    {
        var response = await _client.PostAsJsonAsync(path + "/prompts/preview", new { prompt, label = "Shot 1", sceneLabel = "Scene 1", durationSeconds = 5.5, model = "local-test" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ScenePromptReview>())!;
    }

    [Theory]
    [InlineData("Maya holds a mug.", false)]
    [InlineData("Diagnostic failure", true)]
    public async Task Progress_stream_returns_one_result_or_diagnostic_and_never_writes(string prompt, bool failed)
    {
        var path = await CreateWorkspace();
        var response = await _client.PostAsJsonAsync(path + "/prompts/preview-stream", new { prompt, model = "test" });
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        var events = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        Assert.Equal("progress", events[0].GetProperty("type").GetString());
        Assert.Equal(failed ? "error" : "result", events.Last().GetProperty("type").GetString());
        if (failed)
            Assert.Equal("invalid_property", events.Last().GetProperty("detail").GetProperty("diagnostics").GetProperty("issues")[0].GetProperty("code").GetString());
        else
            Assert.Equal(2, events.Last().GetProperty("review").GetProperty("proposed").GetProperty("entities").GetArrayLength());
        Assert.Empty((await _client.GetFromJsonAsync<SceneWorkspace>(path))!.Entities);
    }

    [Fact]
    public async Task Preview_does_not_write_apply_is_atomic_and_followup_receives_persisted_context()
    {
        var path = await CreateWorkspace(); var review = await Preview(path);
        Assert.Empty((await _client.GetFromJsonAsync<SceneWorkspace>(path))!.Entities);
        Assert.Equal(2, review.Proposed.Entities.Count);
        var apply = await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = review.Preview.Id });
        apply.EnsureSuccessStatusCode();
        var saved = (await apply.Content.ReadFromJsonAsync<SceneWorkspace>())!;
        Assert.Equal("Maya holds a mug.", Assert.Single(saved.Prompts!).Prompt);
        Assert.Equal("Scene 1", saved.Prompts![0].SceneLabel);
        Assert.Equal(5.5, saved.Prompts[0].DurationSeconds);
        Assert.Equal(2, saved.Prompts[0].IntroducedEntityIds!.Count);
        var mug = saved.Entities.Single(e => e.Name == "Mug");
        var query = await _client.PostAsJsonAsync(path + "/query", new SceneQuery(mug.Id));
        Assert.Equal("Maya", Assert.Single((await query.Content.ReadFromJsonAsync<SceneQueryResult>())!.Facts).Value);
        var again = await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = review.Preview.Id });
        Assert.Single((await again.Content.ReadFromJsonAsync<SceneWorkspace>())!.History);
        var next = await Preview(path, "She drops it.");
        Assert.Empty(next.Preview.Extraction.Entities);
        (await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = next.Preview.Id })).EnsureSuccessStatusCode();
        saved = (await _client.GetFromJsonAsync<SceneWorkspace>(path))!;
        Assert.Equal(2, saved.Entities.Count);
        query = await _client.PostAsJsonAsync(path + "/query", new SceneQuery(mug.Id));
        Assert.Empty((await query.Content.ReadFromJsonAsync<SceneQueryResult>())!.Facts);
    }

    [Fact]
    public async Task Shot_metadata_can_be_edited_without_changing_state_or_story_order()
    {
        var path = await CreateWorkspace();
        var review = await Preview(path);
        var applied = await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = review.Preview.Id });
        applied.EnsureSuccessStatusCode();
        var before = (await applied.Content.ReadFromJsonAsync<SceneWorkspace>())!;

        var response = await _client.PutAsJsonAsync(path + $"/prompts/{review.Preview.Id}/metadata",
            new { sceneLabel = "Scene 2 · Garden", shotLabel = "Close-up", durationSeconds = 3.2 });

        response.EnsureSuccessStatusCode();
        var after = (await response.Content.ReadFromJsonAsync<SceneWorkspace>())!;
        Assert.Equal(before.History, after.History);
        Assert.Equal(before.Entities, after.Entities);
        var shot = Assert.Single(after.Prompts!);
        Assert.Equal("Scene 2 · Garden", shot.SceneLabel);
        Assert.Equal("Close-up", shot.Label);
        Assert.Equal(3.2, shot.DurationSeconds);
        Assert.Equal(before.Prompts![0].FirstSequence, shot.FirstSequence);

        var page = await _client.GetStringAsync("/Dashboard/SceneMemoryTimeline");
        Assert.Contains("Shot and scene timeline", page);
        Assert.Contains("scene-memory-timeline.js", page);
    }

    [Fact]
    public async Task Event_only_preview_persists_and_is_available_through_direct_provider_and_agent_queries()
    {
        var path = await CreateWorkspace();
        var initial = await Preview(path);
        (await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = initial.Preview.Id })).EnsureSuccessStatusCode();
        var review = await Preview(path, "Maya considers dropping the mug.");
        Assert.Empty(review.Preview.Extraction.Changes);
        Assert.Single(review.Preview.Extraction.Events!);
        (await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = review.Preview.Id })).EnsureSuccessStatusCode();
        var saved = (await _client.GetFromJsonAsync<SceneWorkspace>(path))!;
        Assert.Single(saved.History.Where(f => f.IsEvent));
        foreach (var entity in saved.Entities)
        {
            var response = await _client.PostAsJsonAsync(path + "/query", new SceneQuery(entity.Id));
            var result = (await response.Content.ReadFromJsonAsync<SceneQueryResult>())!;
            Assert.Equal("intended", Assert.Single(result.Events!).EventMode);
            Assert.Contains(result.Facts, f => f.Property is "held_by" or "holding");
        }
        var provider = await _client.PostAsJsonAsync("/api/scene-memory/query", new { workspaceId = saved.Id, entityId = saved.Entities[1].Id });
        provider.EnsureSuccessStatusCode();
        var json = await provider.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("intended", json.GetProperty("result").GetProperty("events")[0].GetProperty("eventMode").GetString());
        var agentResponse = await _client.PostAsJsonAsync(path + "/agents", new { name = "Event test" });
        agentResponse.EnsureSuccessStatusCode();
        var agent = await agentResponse.Content.ReadFromJsonAsync<JsonElement>();
        var agentQuery = await _client.PostAsJsonAsync(path + "/agents/" + agent.GetProperty("id").GetString() + "/query", new SceneQuery(saved.Entities[0].Id));
        agentQuery.EnsureSuccessStatusCode();
        json = await agentQuery.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("intended", json.GetProperty("result").GetProperty("events")[0].GetProperty("eventMode").GetString());
    }

    [Fact]
    public async Task Human_corrections_are_revalidated_previewed_and_applied_without_changing_server_owned_metadata()
    {
        var path = await CreateWorkspace();
        var original = await Preview(path);
        var corrected = original.Preview.Extraction with
        {
            Entities = original.Preview.Extraction.Entities.Select(e =>
                e.Ref == "mug" ? e with { Name = "Blue mug" } : e).ToArray(),
            Changes = new[]
            {
                new ScenePromptChange("mug", "held_by", "", "maya", false, "Maya holds a mug"),
                new ScenePromptChange("mug", "color", "blue", "", false, "mug")
            },
            Events = new[]
            {
                new ScenePromptEvent("Maya holds the blue mug", new[] { "maya", "mug" }, "Maya holds a mug")
            }
        };
        var response = await _client.PostAsJsonAsync(path + "/prompts/edit", new
        {
            previewId = original.Preview.Id,
            extraction = corrected
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var edited = (await response.Content.ReadFromJsonAsync<ScenePromptReview>())!;
        Assert.Equal(original.Preview.Id, edited.Preview.Id);
        Assert.Equal(original.Preview.Prompt, edited.Preview.Prompt);
        Assert.Equal(original.Preview.Model, edited.Preview.Model);
        Assert.Equal(original.Preview.Revision, edited.Preview.Revision);
        Assert.Contains(edited.Proposed.History, f => f.Property == "color" && f.Value == "blue");
        Assert.Empty((await _client.GetFromJsonAsync<SceneWorkspace>(path))!.History);

        response = await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = original.Preview.Id });
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<SceneWorkspace>())!;
        Assert.Contains(saved.Entities, e => e.Name == "Blue mug");
        Assert.Contains(saved.History, f => f.Property == "color" && f.Value == "blue");
        Assert.Contains(saved.History, f => f.IsEvent && f.Value == "Maya holds the blue mug");
    }

    [Fact]
    public async Task Invalid_correction_does_not_replace_the_last_valid_preview_or_write_memory()
    {
        var path = await CreateWorkspace();
        var original = await Preview(path);
        var invalid = original.Preview.Extraction with
        {
            Changes = new[] { new ScenePromptChange("mug", "color", "blue", "", false, "not in prompt") }
        };
        var response = await _client.PostAsJsonAsync(path + "/prompts/edit", new
        {
            previewId = original.Preview.Id,
            extraction = invalid
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<SceneWorkspace>(path))!.History);

        // The failed edit cannot poison the server-owned cached preview.
        response = await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = original.Preview.Id });
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<SceneWorkspace>())!;
        Assert.Contains(saved.Entities, e => e.Name == "Mug");
        Assert.DoesNotContain(saved.History, f => f.Property == "color");
    }

    [Fact]
    public async Task Ambiguity_expired_foreign_and_stale_previews_leave_memory_unchanged()
    {
        var path = await CreateWorkspace(); var other = await CreateWorkspace();
        var ambiguous = await Preview(path, "Which mug?");
        Assert.Single(ambiguous.Preview.Extraction.Questions);
        var response = await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = ambiguous.Preview.Id });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var review = await Preview(path);
        response = await _client.PostAsJsonAsync(other + "/prompts/apply", new { previewId = review.Preview.Id });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        response = await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = "unknown" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        (await _client.PostAsJsonAsync(path + "/entities", new { name = "Alex", kind = "Character" })).EnsureSuccessStatusCode();
        response = await _client.PostAsJsonAsync(path + "/prompts/apply", new { previewId = review.Preview.Id });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<SceneWorkspace>(path))!.History);
    }

    [Fact]
    public async Task Failed_interpretation_returns_source_diagnostics_and_attempt_history_without_saving()
    {
        var path = await CreateWorkspace();
        var response = await _client.PostAsJsonAsync(path + "/prompts/preview", new { prompt = "Diagnostic failure", model = "local-test" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var diagnostics = result.GetProperty("diagnostics");
        Assert.Equal("Diagnostic failure", diagnostics.GetProperty("prompt").GetString());
        Assert.Equal("local-test", diagnostics.GetProperty("model").GetString());
        Assert.Equal(3, diagnostics.GetProperty("attempts").GetArrayLength());
        Assert.Equal("changes[2]", diagnostics.GetProperty("issues")[0].GetProperty("path").GetString());
        Assert.Equal(0, diagnostics.GetProperty("issues")[0].GetProperty("sourceStart").GetInt32());
        Assert.Empty((await _client.GetFromJsonAsync<SceneWorkspace>(path))!.History);
        Assert.Empty((await _client.GetFromJsonAsync<SceneWorkspace>(path))!.Entities);
    }

    [Fact]
    public async Task Unavailable_model_reports_a_useful_error_and_page_exposes_prompt_workflow()
    {
        var path = await CreateWorkspace();
        var response = await _client.PostAsJsonAsync(path + "/prompts/preview", new { prompt = "Offline" });
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Ollama", await response.Content.ReadAsStringAsync());
        Assert.Empty((await _client.GetFromJsonAsync<SceneWorkspace>(path))!.Entities);
        var page = await _client.GetStringAsync("/Dashboard/SceneMemory");
        Assert.Contains("Preview scene changes", page);
        Assert.Contains("Apply to scene", page);
        Assert.Contains("What will change", page);
        Assert.Contains("Check changes", page);
        Assert.Contains("Advanced · detailed changes and JSON", page);
        Assert.Contains("Test ambiguity", page);
        Assert.Contains("sm-preview-progress", page);
        Assert.Contains("Cancel preview", page);
    }
}

public sealed class ScenePromptWebApplicationFactory : SceneMemoryWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<IScenePromptInterpreter, TestScenePromptInterpreter>());
    }
}

public sealed class TestScenePromptInterpreter : IScenePromptInterpreter
{
    public Task<ScenePromptExtraction> InterpretAsync(SceneWorkspace workspace, string prompt, string model,
        IProgress<ScenePromptProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new("interpreting", 1, 1, 0, "Interpreting section 1 of 1."));
        return InterpretAsync(workspace, prompt, model, ct);
    }

    public Task<ScenePromptExtraction> InterpretAsync(SceneWorkspace workspace, string prompt, string model, CancellationToken ct)
    {
        if (prompt == "Offline") throw new HttpRequestException("Test model unavailable.");
        if (prompt == "Diagnostic failure")
        {
            var issue = ScenePromptDiagnostics.Issue(prompt, "invalid_property", "changes[2]", "Invalid property.", evidence: prompt);
            throw new ScenePromptInterpretationException("Test failure", model, prompt,
                Enumerable.Range(1, 3).Select(i => new ScenePromptAttempt(i, new[] { issue })).ToArray());
        }
        if (prompt == "Which mug?") return Task.FromResult(new ScenePromptExtraction(Array.Empty<ScenePromptEntity>(),
            Array.Empty<ScenePromptChange>(), new[] { "Do you mean the red or blue mug?" }));
        if (prompt == "Maya considers dropping the mug.")
            return Task.FromResult(new ScenePromptExtraction(Array.Empty<ScenePromptEntity>(), Array.Empty<ScenePromptChange>(),
                Array.Empty<string>(), new[] { new ScenePromptEvent(prompt, workspace.Entities.Select(e => e.Id).ToArray(), prompt, "intended") }));
        if (prompt == "She drops it.")
        {
            Assert.Equal("Maya holds a mug.", Assert.Single(workspace.Prompts!).Prompt);
            return Task.FromResult(new ScenePromptExtraction(Array.Empty<ScenePromptEntity>(), new[] {
                new ScenePromptChange(workspace.Entities.Single(e => e.Name == "Mug").Id, "held_by", "", "", true, prompt) }, Array.Empty<string>()));
        }
        return Task.FromResult(new ScenePromptExtraction(new[] { new ScenePromptEntity("maya", "Maya", "Character"), new ScenePromptEntity("mug", "Mug", "Object") },
            new[] { new ScenePromptChange("mug", "held_by", "", "maya", false, prompt) }, Array.Empty<string>()));
    }
}

public sealed class OllamaScenePromptInterpreterTests
{
    [Fact]
    public async Task Evidence_typography_is_repaired_without_an_extra_model_call()
    {
        var handler = new DiagnosticHandler("typography");
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(new HttpClient(handler));
        var w = new SceneWorkspace("w", "Test", DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        var result = await new OllamaScenePromptInterpreter(factory.Object).InterpretAsync(w, DiagnosticHandler.Prompt, "test", default);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(DiagnosticHandler.Prompt, result.Changes[0].Evidence);
        Assert.Empty(w.History);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task All_field_diagnostics_are_sent_to_repair_and_retained_on_failure(bool alwaysInvalid)
    {
        var handler = new DiagnosticHandler(alwaysInvalid ? "invalid" : "repair");
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(new HttpClient(handler));
        var w = new SceneWorkspace("w", "Test", DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        var interpreter = new OllamaScenePromptInterpreter(factory.Object);
        if (alwaysInvalid)
        {
            var error = await Assert.ThrowsAsync<ScenePromptInterpretationException>(() => interpreter.InterpretAsync(w, DiagnosticHandler.Prompt, "test", default));
            Assert.Equal(3, error.Attempts.Count);
            Assert.All(error.Attempts, a => {
                Assert.Contains(a.Issues, i => i.Code == "invalid_property");
                Assert.Contains(a.Issues, i => i.Code == "evidence_mismatch" && i.SourceStart == null);
            });
        }
        else
        {
            var result = await interpreter.InterpretAsync(w, DiagnosticHandler.Prompt, "test", default);
            Assert.Equal("speech", result.Changes[0].Property);
            Assert.Equal(2, handler.Calls);
        }
        Assert.Empty(w.Entities);
    }

    private sealed class DiagnosticHandler(string mode) : HttpMessageHandler
    {
        public const string Prompt = "Raha says “Don’t go!”";
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (Calls > 1)
            {
                var payload = await request.Content!.ReadAsStringAsync(ct);
                Assert.Contains("invalid_property", payload);
                Assert.Contains("evidence_mismatch", payload);
                Assert.Contains("changes[0]", payload);
            }
            var invalid = mode == "invalid" || mode == "repair" && Calls == 1;
            var extraction = new ScenePromptExtraction(new[] { new ScenePromptEntity("r", "Raha", "Character") },
                new[] { new ScenePromptChange("r", invalid ? "Speech Text" : "speech", "Don’t go!", "", false,
                    invalid ? "Raha asks Ryan to stay" : "Raha says \"Don't go!\"") }, Array.Empty<string>());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new {
                content = JsonSerializer.Serialize(extraction, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }) };
        }
    }

    [Theory]
    [InlineData("reversed", false)]
    [InlineData("animal_type", false)]
    [InlineData("restraint", false)]
    [InlineData("restraint", true)]
    public async Task Model_repairs_cast_and_possession_even_after_completeness_review(string failure, bool reviewFirst)
    {
        var handler = new PossessionRepairHandler(failure, reviewFirst, false);
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(new HttpClient(handler));
        var workspace = new SceneWorkspace("scene", "Scene", DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        var result = await new OllamaScenePromptInterpreter(factory.Object).InterpretAsync(workspace, PossessionRepairHandler.Prompt, "test", default);
        Assert.Equal(reviewFirst ? 4 : 3, handler.Calls);
        Assert.Contains(result.Entities, e => e.Name == "Raha" && e.Kind == "Character");
        Assert.Contains(result.Entities, e => e.Name == "Ryan" && e.Kind == "Character");
        Assert.Contains(result.Entities, e => e.Name == "Earth Monster" && e.Kind == "Character");
        var proposed = ScenePromptReducer.Apply(workspace, new("preview", ScenePromptReducer.Revision(workspace),
            PossessionRepairHandler.Prompt, "", "test", result));
        foreach (var name in new[] { "Raha", "Ryan" })
        {
            var character = proposed.Entities.Single(e => e.Name == name);
            Assert.Single(proposed.History.Where(f => f.Property == "held_by" && f.RelatedEntityId == character.Id));
            Assert.Contains(proposed.History, f => f.EntityId == character.Id && f.Property == "stuck_in");
        }
        Assert.Empty(workspace.Entities);
        Assert.Empty(workspace.History);
    }

    [Fact]
    public async Task Exhausted_repairs_identify_offending_relationship_without_guessing_or_saving()
    {
        var handler = new PossessionRepairHandler("restraint", false, true);
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(new HttpClient(handler));
        var workspace = new SceneWorkspace("scene", "Scene", DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        var error = await Assert.ThrowsAnyAsync<ArgumentException>(() => new OllamaScenePromptInterpreter(factory.Object)
            .InterpretAsync(workspace, PossessionRepairHandler.Prompt, "test", default));
        Assert.Equal(3, handler.Calls);
        Assert.Contains("'Raha' (Character) held_by 'Slime' (Object)", error.Message);
        Assert.Contains("Raha and Ryan are trapped in slime", error.Message);
        Assert.Contains("two correction attempts", error.Message);
        Assert.Empty(workspace.Entities);
    }

    private sealed class PossessionRepairHandler(string failure, bool reviewFirst, bool alwaysInvalid) : HttpMessageHandler
    {
        public const string Prompt = "Raha and Ryan are chipmunk characters. The Earth Monster approaches. They hold matching swords. Raha and Ryan are trapped in slime.";
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var invalid = alwaysInvalid || Calls < (reviewFirst ? 4 : 3);
            var entities = new[] {
                new ScenePromptEntity("raha", "Raha", failure == "animal_type" && invalid ? "Object" : "Character"),
                new ScenePromptEntity("ryan", "Ryan", "Character"),
                new ScenePromptEntity("monster", "Earth Monster", "Character"),
                new ScenePromptEntity("slime", "Slime", "Object"),
                new ScenePromptEntity("sword_a", "Raha's sword", "Object"),
                new ScenePromptEntity("sword_b", "Ryan's sword", "Object") };
            var changes = new[] {
                new ScenePromptChange("sword_a", "held_by", "", "raha", false, "They hold matching swords"),
                new ScenePromptChange("sword_b", "held_by", "", "ryan", false, "They hold matching swords"),
                new ScenePromptChange("raha", "stuck_in", "", "slime", false, "Raha and Ryan are trapped in slime"),
                new ScenePromptChange("ryan", "stuck_in", "", "slime", false, "Raha and Ryan are trapped in slime") };
            if (invalid && failure == "reversed") changes[0] = changes[0] with { Subject = "raha", Target = "sword_a" };
            if (invalid && failure == "restraint") changes[2] = changes[2] with { Property = "held_by" };
            if (reviewFirst && Calls == 1) changes = Array.Empty<ScenePromptChange>();
            var payload = await request.Content!.ReadAsStringAsync(ct);
            if (Calls > (reviewFirst ? 2 : 1))
            {
                using var json = JsonDocument.Parse(payload);
                Assert.Equal(4, json.RootElement.GetProperty("messages").GetArrayLength());
                Assert.Contains(Prompt, json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
                Assert.Contains("Invalid possession", payload);
                Assert.Contains("Source:", payload);
                Assert.Contains("Correct types of NEW entities", payload);
            }
            var result = new ScenePromptExtraction(entities, changes, Array.Empty<string>(), new[] {
                new ScenePromptEvent("The Earth Monster approaches", new[] { "Earth Monster" }, "The Earth Monster approaches") });
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new {
                content = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }) };
        }
    }

    [Theory]
    [InlineData("invented evidence", "intended", "raha")]
    [InlineData("Raha considers waving", "planned", "raha")]
    [InlineData("Raha considers waving", "intended", "unknown")]
    public async Task Invalid_events_are_sent_back_to_model_and_never_silently_dropped_or_asserted_as_completed(string evidence, string mode, string participant)
    {
        var handler = new InvalidEventHandler(evidence, mode, participant);
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(new HttpClient(handler));
        var workspace = new SceneWorkspace("scene", "Scene", DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new OllamaScenePromptInterpreter(factory.Object)
            .InterpretAsync(workspace, "Raha considers waving", "test", default));
        Assert.Equal(3, handler.Calls);
        Assert.Empty(workspace.History);
    }

    private sealed class InvalidEventHandler(string evidence, string mode, string participant) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var result = new ScenePromptExtraction(new[] { new ScenePromptEntity("raha", "Raha", "Character") },
                Array.Empty<ScenePromptChange>(), Array.Empty<string>(), new[] {
                    new ScenePromptEvent("Raha considers waving", new[] { participant }, evidence, mode) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new {
                content = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }) });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interpreter_rejects_text_ownership_and_requests_discovery_of_the_missing_object(bool stillInvalid)
    {
        var handler = new OwnershipHandler(stillInvalid);
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(new HttpClient(handler));
        var workspace = new SceneWorkspace("scene", "Scene", DateTimeOffset.UtcNow,
            new[] { new SceneEntity("a", "ActorA", "Character") }, Array.Empty<SceneFact>());
        var interpreter = new OllamaScenePromptInterpreter(factory.Object);
        if (stillInvalid)
            await Assert.ThrowsAnyAsync<ArgumentException>(() => interpreter.InterpretAsync(workspace, "ActorA owns a red hat", "test", default));
        else
        {
            var result = await interpreter.InterpretAsync(workspace, "ActorA owns a red hat", "test", default);
            var proposed = ScenePromptReducer.Apply(workspace, new("p", ScenePromptReducer.Revision(workspace), "ActorA owns a red hat", "", "test", result));
            var hat = Assert.Single(proposed.Entities.Where(e => e.Kind == "Object"));
            Assert.Equal("Red hat", hat.Name);
            Assert.Contains(proposed.History, f => f.EntityId == hat.Id && f.Property == "owned_by" && f.RelatedEntityId == "a");
            Assert.Contains(proposed.History, f => f.EntityId == hat.Id && f.Property == "color" && f.Value == "red");
            Assert.DoesNotContain(proposed.History, f => f.Property == "held_by");
        }
        Assert.Equal(stillInvalid ? 3 : 2, handler.Calls);
        Assert.Empty(workspace.History);
    }

    private sealed class OwnershipHandler(bool stillInvalid) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var payload = await request.Content!.ReadAsStringAsync(ct);
            if (Calls == 1) Assert.Contains("OWNERSHIP EXAMPLE", payload);
            if (Calls == 2) Assert.Contains("Discover the owned object", payload);
            var extraction = Calls == 1 || stillInvalid
                ? new ScenePromptExtraction(Array.Empty<ScenePromptEntity>(), new[] { new ScenePromptChange("a", "owned_by", "red hat", "", false, "ActorA owns a red hat") }, Array.Empty<string>())
                : new ScenePromptExtraction(new[] { new ScenePromptEntity("hat", "Red hat", "Object") }, new[] {
                    new ScenePromptChange("hat", "owned_by", "", "a", false, "ActorA owns a red hat"),
                    new ScenePromptChange("hat", "color", "red", "", false, "red hat") }, Array.Empty<string>());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new { content = JsonSerializer.Serialize(extraction, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }) };
        }
    }

    [Fact]
    public async Task Interpreter_repairs_action_only_output_before_returning_a_preview()
    {
        var handler = new RepairHandler();
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(new HttpClient(handler));
        var workspace = new SceneWorkspace("scene", "Scene", DateTimeOffset.UtcNow,
            new[] { new SceneEntity("a", "ActorA", "Character"), new SceneEntity("b", "Bread", "Object") },
            new[] { new SceneFact(1, "b", "held_by", "ActorA", "initial", DateTimeOffset.UtcNow, "a") });
        var extraction = await new OllamaScenePromptInterpreter(factory.Object).InterpretAsync(workspace, "ActorA eats the bread", "local-model", default);
        Assert.Equal(2, handler.Calls);
        Assert.Contains(extraction.Changes, c => c.Subject == "b" && c.Property == "status" && c.Value == "consumed");
        Assert.Contains("Validation rejected", handler.LastMessages);
    }

    [Fact]
    public async Task Interpreter_asks_model_to_repair_cinematic_relationships_and_preserves_separate_props()
    {
        var handler = new CinematicActionHandler();
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(new HttpClient(handler));
        var workspace = new SceneWorkspace("scene", "Scene", DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        const string prompt = "Raha and Ryan are trapped in slime, holding their swords and facing the Earth Monster.";

        var extraction = await new OllamaScenePromptInterpreter(factory.Object).InterpretAsync(workspace, prompt, "local-model", default);

        Assert.Equal(2, handler.Calls);
        Assert.DoesNotContain(extraction.Changes, c => c.Property == "action");
        Assert.Contains(extraction.Changes, c => c.Subject == "raha" && c.Property == "facing" && c.Target == "monster");
        Assert.Contains(extraction.Events!, e => e.Participants.Contains("raha") && e.Participants.Contains("monster"));
        Assert.Equal(2, extraction.Entities.Count(e => e.Name.Contains("sword", StringComparison.OrdinalIgnoreCase)));
        var proposed = ScenePromptReducer.Apply(workspace, new("preview", ScenePromptReducer.Revision(workspace), prompt, "", "local-model", extraction));
        Assert.Contains(proposed.History, f => f.Property == "facing" && f.RelatedEntityId == proposed.Entities.Single(e => e.Name == "Earth Monster").Id);
        Assert.Equal(2, proposed.History.Where(f => f.Property == "held_by" && !f.Removed).Select(f => f.RelatedEntityId).Distinct().Count());
    }

    private sealed class CinematicActionHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var extraction = new ScenePromptExtraction(new[] {
                new ScenePromptEntity("raha", "Raha", "Character"),
                new ScenePromptEntity("ryan", "Ryan", "Character"),
                new ScenePromptEntity("slime", "Slime", "Object"),
                new ScenePromptEntity("monster", "Earth Monster", "Character"),
                new ScenePromptEntity("sword", "Glowing sword", "Object") }, new[] {
                    new ScenePromptChange("raha", "stuck_in", "", "slime", false, "trapped in slime"),
                    new ScenePromptChange("ryan", "stuck_in", "", "slime", false, "trapped in slime"),
                    new ScenePromptChange("raha", "holding", "", "sword", false, "holding their swords"),
                    new ScenePromptChange("ryan", "holding", "", "sword", false, "holding their swords"),
                    new ScenePromptChange("raha", Calls == 1 ? "action" : "facing", "", "Earth Monster", false, "facing the Earth Monster") },
                Array.Empty<string>(), new[] {
                    new ScenePromptEvent("Raha faces the Earth Monster", new[] { "Raha", "Earth Monster" }, "facing the Earth Monster", "ongoing") });
            if (Calls > 1)
                extraction = extraction with {
                    Entities = extraction.Entities.Append(new ScenePromptEntity("ryan_sword", "Ryan's sword", "Object")).ToArray(),
                    Changes = extraction.Changes.Select(c => c.Subject == "ryan" && c.Property == "holding"
                        ? c with { Target = "ryan_sword" } : c).ToArray()
                };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new {
                content = JsonSerializer.Serialize(extraction, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }) });
        }
    }

    [Fact]
    public async Task Interpreter_reviews_events_with_missing_state_effects()
    {
        var handler = new CoverageHandler();
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(new HttpClient(handler));
        var workspace = new SceneWorkspace("scene", "Scene", DateTimeOffset.UtcNow, new[] { new SceneEntity("c", "ActorC", "Character") }, Array.Empty<SceneFact>());
        var result = await new OllamaScenePromptInterpreter(factory.Object).InterpretAsync(workspace, "ActorC can sing but got sick", "test", default);
        Assert.Equal(2, handler.Calls);
        Assert.Contains(result.Changes, c => c.Property == "health" && c.Value == "sick");
        Assert.Contains(result.Changes, c => c.Property == "ability_sing");
    }

    private sealed class CoverageHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (Calls == 2) Assert.Contains("missing STATE CHANGES", await request.Content!.ReadAsStringAsync(ct));
            var extraction = new ScenePromptExtraction(Array.Empty<ScenePromptEntity>(), Calls == 1 ? Array.Empty<ScenePromptChange>() : new[] {
                new ScenePromptChange("c", "health", "sick", "", false, "got sick"),
                new ScenePromptChange("c", "ability_sing", "true", "", false, "can sing") }, Array.Empty<string>(),
                new[] { new ScenePromptEvent("ActorC got sick", new[] { "c" }, "got sick") });
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new { content = JsonSerializer.Serialize(extraction,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }) };
        }
    }

    private sealed class RepairHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string LastMessages { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastMessages = await request.Content!.ReadAsStringAsync(ct);
            var changes = Calls == 1 ? new[] { new ScenePromptChange("a", "action", "", "b", false, "ActorA eats the bread") }
                : new[] { new ScenePromptChange("b", "status", "consumed", "", false, "ActorA eats the bread"),
                    new ScenePromptChange("b", "consumed_by", "", "a", false, "ActorA eats the bread") };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new { content = JsonSerializer.Serialize(
                new ScenePromptExtraction(Array.Empty<ScenePromptEntity>(), changes, Array.Empty<string>()), new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }) };
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transport_requests_structured_output_and_rejects_malformed_model_json(bool malformed)
    {
        var handler = new CaptureHandler(malformed);
        var client = new HttpClient(handler);
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(client);
        var interpreter = new OllamaScenePromptInterpreter(factory.Object);
        var workspace = new SceneWorkspace("scene", "Scene", DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        if (malformed) await Assert.ThrowsAnyAsync<ArgumentException>(() => interpreter.InterpretAsync(workspace, "A scene", "local-model", default));
        else Assert.Empty((await interpreter.InterpretAsync(workspace, "A scene", "local-model", default)).Changes);
        Assert.Equal("local-model", handler.Payload.GetProperty("model").GetString());
        Assert.False(handler.Payload.GetProperty("stream").GetBoolean());
        Assert.Equal(0, handler.Payload.GetProperty("options").GetProperty("presence_penalty").GetInt32());
        Assert.Equal(0, handler.Payload.GetProperty("options").GetProperty("frequency_penalty").GetInt32());
        Assert.Equal(1, handler.Payload.GetProperty("options").GetProperty("repeat_penalty").GetInt32());
        Assert.Equal("object", handler.Payload.GetProperty("format").GetProperty("type").GetString());
        Assert.Contains("A scene", handler.Payload.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    private sealed class CaptureHandler(bool malformed) : HttpMessageHandler
    {
        public JsonElement Payload { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new {
                content = malformed ? "{broken" : "{\"entities\":[],\"changes\":[],\"questions\":[]}" } }) };
        }
    }
}
