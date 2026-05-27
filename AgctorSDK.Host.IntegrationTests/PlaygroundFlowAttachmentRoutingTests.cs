using AgctorSDK.Host.Services.ProjectMemory;
using AgctorSDK.Host.Services.Scenarios;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class PlaygroundFlowAttachmentRoutingTests
{
    private static PlaygroundFlowRoutingContext PhotoCtx(int count = 1) =>
        PlaygroundFlowRoutingContextBuilder.Build(count, "caption", "raha");

    private static ScenarioFlowRouterPersonaCandidate[] AllCandidates() =>
    [
        new("n1", "person-extractor", null, "e1", null),
        new("n2", "person-query", null, "e2", null),
        new("n3", "style-coach", null, "e3", null),
        new("n4", "fitness-coach", null, "e4", null),
        new("n5", "relationship-coach", null, "e5", null)
    ];

    [Fact]
    public void TryPickPersona_with_save_intent_prefers_person_extractor()
    {
        var ok = PlaygroundFlowAttachmentRouting.TryPickPersona(
            PhotoCtx(),
            "this is Raha",
            AllCandidates(),
            out var persona);

        ok.Should().BeTrue();
        persona.Should().Be("person-extractor");
    }

    [Fact]
    public void TryPickPersona_with_style_photo_prefers_style_coach()
    {
        var ok = PlaygroundFlowAttachmentRouting.TryPickPersona(
            PhotoCtx(),
            "what should I wear with this outfit?",
            AllCandidates(),
            out var persona);

        ok.Should().BeTrue();
        persona.Should().Be("style-coach");
    }

    [Fact]
    public void TryPickPersona_with_fitness_photo_prefers_fitness_coach()
    {
        var ok = PlaygroundFlowAttachmentRouting.TryPickPersona(
            PhotoCtx(),
            "leg day progress check",
            AllCandidates(),
            out var persona);

        ok.Should().BeTrue();
        persona.Should().Be("fitness-coach");
    }

    [Fact]
    public void TryPickPersona_skips_bare_yes_no()
    {
        var candidates = new[]
        {
            new ScenarioFlowRouterPersonaCandidate("n2", "person-extractor", null, "e2", null)
        };

        var ok = PlaygroundFlowAttachmentRouting.TryPickPersona(PhotoCtx(), "yes", candidates, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void BuildRoutingAppendix_includes_structured_context()
    {
        var ctx = PlaygroundFlowRoutingContextBuilder.Build(2, "leg day", "raha");
        var text = PlaygroundFlowAttachmentRouting.BuildRoutingAppendix(ctx);

        text.Should().Contain("attachmentCount: 2");
        text.Should().Contain("style-coach");
        text.Should().Contain("raha");
    }
}
