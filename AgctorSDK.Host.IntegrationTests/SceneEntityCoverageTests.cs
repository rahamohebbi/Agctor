using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Services;
using Moq;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneEntityCoverageTests
{
    [Theory]
    [InlineData("compass")]
    [InlineData("violin")]
    [InlineData("nebulizer")]
    [InlineData("sword")]
    public async Task Source_review_recovers_an_entirely_omitted_prop_and_setting(string prop)
    {
        var prompt = $"Maya is happy while carrying a {prop} in the observatory.";
        var handler = new InventoryHandler(prompt, prop);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OllamaScenePromptInterpreter.ClientName)).Returns(() => new HttpClient(handler, false));
        var workspace = new SceneWorkspace("w", "Story", DateTimeOffset.UtcNow, [new("maya", "Maya", "Character")], []);
        using var interpreter = new OllamaScenePromptInterpreter(factory.Object);
        var result = await interpreter.InterpretAsync(workspace, prompt, "test", default);
        Assert.Equal(2, handler.Calls);
        var proposed = ScenePromptReducer.Apply(workspace, new("shot", ScenePromptReducer.Revision(workspace), prompt, "", "test", result));
        var item = Assert.Single(proposed.Entities.Where(e => e.Kind == "Object"));
        Assert.Equal(prop, item.Name);
        Assert.Single(proposed.Entities.Where(e => e.Kind == "Location"));
        Assert.Contains(proposed.History, f => f.EntityId == item.Id && f.Property == "held_by" && f.RelatedEntityId == "maya");
        Assert.Empty(workspace.History);
        Assert.Single(workspace.Entities);
    }

    private sealed class InventoryHandler(string prompt, string prop) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var payload = await request.Content!.ReadAsStringAsync(ct);
            if (Calls == 2)
            {
                Assert.Contains("missing ENTITIES", payload);
                Assert.Contains("missing entirely", payload);
                Assert.Contains("production instructions", payload);
                Assert.Contains(prompt, payload);
            }
            // The original plan is valid and has a character state change: the old
            // output-only completeness heuristic never requested a review for it.
            var extraction = new ScenePromptExtraction(
                Calls == 1 ? [] : [new("prop", prop, "Object"), new("place", "Observatory", "Location")],
                Calls == 1 ? [new("maya", "mood", "happy", "", false, prompt)] : [
                    new("maya", "mood", "happy", "", false, prompt),
                    new("prop", "held_by", "", "maya", false, prompt),
                    new("maya", "located_at", "", "place", false, prompt)], []);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new {
                content = JsonSerializer.Serialize(extraction, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }) };
        }
    }
}
