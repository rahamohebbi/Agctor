using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.VideoWorkflow;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneMusicInterpretationTests
{
    private const string Prompt = "A 12-year-old boy from Tehran, Iran wakes up in his bed. He stretches and sits up slowly, looking around the room with a gentle expression. Slow, beautiful music plays softly in the background.";

    [Fact]
    public async Task Orphan_music_event_is_repaired_without_losing_soundtrack_or_inventing_a_character()
    {
        var handler = new MusicHandler();
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(() => new HttpClient(handler, false));
        using var interpreter = new OllamaScenePromptInterpreter(clients.Object);
        var workspace = new SceneWorkspace("w", "Story", DateTimeOffset.UtcNow, [], []);
        var result = await interpreter.InterpretAsync(workspace, Prompt, "test", default);
        Assert.Equal(3, handler.Calls); // invalid, repaired, completeness review
        var proposed = ScenePromptReducer.Apply(workspace, new("shot", ScenePromptReducer.Revision(workspace), Prompt, "", "test", result));
        Assert.Single(proposed.Entities.Where(e => e.Kind == "Character"));
        var music = Assert.Single(proposed.Entities.Where(e => e.Kind == "Music"));
        Assert.Contains(proposed.History, f => f.EntityId == music.Id && f.Property == "playback" && f.Value == "playing softly");
        Assert.Contains(proposed.History, f => f.IsEvent && f.ParticipantIds!.Contains(music.Id));
        Assert.Empty(workspace.Entities);

        // Later previews must retain participants introduced by earlier previews, even if their
        // concrete name was chosen by the planner and does not occur in the user's original idea.
        var state = new SceneVideoState { Input = new("r", "w", "His morning routine", StoryMode: "new"),
            Clips = [new() { ShotId = "shot" }], Current = 0 };
        var scoped = SceneVideoMemoryScope.Select(proposed, state, completedClips: 1);
        Assert.Contains(scoped.Entities, e => e.Id == music.Id);
        Assert.Single(scoped.Prompts!);
    }

    private sealed class MusicHandler : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = body.RootElement;
            var participants = root.GetProperty("format").GetProperty("properties").GetProperty("events")
                .GetProperty("items").GetProperty("properties").GetProperty("participants");
            Assert.Equal(1, participants.GetProperty("minItems").GetInt32());
            Assert.Equal(20, participants.GetProperty("maxItems").GetInt32());
            var system = root.GetProperty("messages")[0].GetProperty("content").GetString()!;
            Assert.Contains("Music", system);
            if (Calls == 2) Assert.Contains("music/sound event has no participants", system);
            var extraction = new ScenePromptExtraction(
                Calls == 1 ? [new("boy", "Boy", "Character")] : [new("boy", "Boy", "Character"), new("score", "Gentle soundtrack", "Music")],
                Calls == 1 ? [] : [new("score", "playback", "playing softly", "", false, Prompt)], [],
                [new("The boy wakes up", ["boy"], Prompt),
                 new("Slow, beautiful music plays softly in the background", Calls == 1 ? [] : ["score"], Prompt, "ongoing")]);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new {
                content = JsonSerializer.Serialize(extraction, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }) };
        }
    }
}
