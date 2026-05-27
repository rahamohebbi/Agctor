using AgctorSDK.Host.Services.ProjectMemory;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class PlaygroundFlowPreRouterTests
{
    private static PlaygroundFlowRoutingContext Ctx => PlaygroundFlowRoutingContextBuilder.Build(1, null, null);

    private static ScenarioFlowRouterPersonaCandidate[] Candidates(params string[] ids) =>
        ids.Select(id => new ScenarioFlowRouterPersonaCandidate("n", id, null, "e", null)).ToArray();

    [Theory]
    [InlineData("how does this outfit look?", "style-coach")]
    [InlineData("gym form check on my squat", "fitness-coach")]
    [InlineData("please save this photo", "person-extractor")]
    [InlineData("who is in this picture?", "person-query")]
    public void TryPickPersona_routes_by_intent(string message, string expected)
    {
        var ok = PlaygroundFlowPreRouter.TryPickPersona(
            Ctx,
            message,
            Candidates("style-coach", "fitness-coach", "person-extractor", "person-query"),
            out var persona);

        ok.Should().BeTrue();
        persona.Should().Be(expected);
    }

    [Fact]
    public void InferSuggestedIntent_detects_style_and_fitness()
    {
        PlaygroundFlowPreRouter.InferSuggestedIntent("love this dress").Should().Be("style");
        PlaygroundFlowPreRouter.InferSuggestedIntent("leg day").Should().Be("fitness");
    }
}
