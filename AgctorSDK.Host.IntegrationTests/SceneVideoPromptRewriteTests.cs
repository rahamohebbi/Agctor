using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgctorSDK.Host.Services;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneVideoPromptRewriteTests
{
    [Fact]
    public async Task Rewrite_sends_prompt_and_instruction_as_data_and_returns_reviewable_draft()
    {
        var handler = new Reply();
        using var http = new HttpClient(handler);
        var result = await SceneVideoPromptRewrite.RewriteAsync(new("Raha smiles for 12 seconds.", "Make Raha ten years younger.", "test-chat"), http, default);
        Assert.Equal("Younger Raha smiles for 12 seconds.", result.Prompt);
        Assert.Equal("test-chat", result.Model);
        Assert.False(handler.Request.GetProperty("stream").GetBoolean());
        Assert.False(handler.Request.GetProperty("think").GetBoolean());
        using var input = JsonDocument.Parse(handler.Request.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal("Raha smiles for 12 seconds.", input.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("Make Raha ten years younger.", input.RootElement.GetProperty("instruction").GetString());
    }

    [Theory]
    [InlineData("", "edit")]
    [InlineData("prompt", " ")]
    public async Task Invalid_input_never_calls_the_model(string prompt, string instruction)
    {
        var handler = new Reply(); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => SceneVideoPromptRewrite.RewriteAsync(new(prompt, instruction), http, default));
        Assert.Equal(JsonValueKind.Undefined, handler.Request.ValueKind);
    }

    [Theory]
    [InlineData("{\"message\":{\"content\":\"{\\\"prompt\\\":\\\" \\\"}\"}}")]
    [InlineData("{\"message\":{\"content\":\"not json\"}}")]
    [InlineData("{\"message\":{\"content\":\"{\\\"prompt\\\":42}\"}}")]
    [InlineData("{\"message\":{\"content\":\"{}\"}}")]
    [InlineData("{\"done_reason\":\"length\"}")]
    public void Incomplete_suggestions_are_rejected_without_replacing_the_draft(string json)
    {
        using var body = JsonDocument.Parse(json);
        Assert.Contains("unchanged", Assert.Throws<ArgumentException>(() => SceneVideoPromptRewrite.ReadPrompt(body.RootElement)).Message);
    }

    [Fact]
    public async Task Cancellation_stops_work_and_does_not_lock_the_next_edit()
    {
        using var http = new HttpClient(new Reply());
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SceneVideoPromptRewrite.RewriteAsync(new("prompt", "edit", "test"), http, cancel.Token));
        Assert.NotEmpty((await SceneVideoPromptRewrite.RewriteAsync(new("prompt", "edit", "test"), http, default)).Prompt);
    }

    private sealed class Reply : HttpMessageHandler
    {
        public JsonElement Request { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new {
                content = JsonSerializer.Serialize(new { prompt = "Younger Raha smiles for 12 seconds." }) } }) };
        }
    }
}
