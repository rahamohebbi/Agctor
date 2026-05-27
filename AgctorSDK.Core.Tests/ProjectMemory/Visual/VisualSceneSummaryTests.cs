using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Visual;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory.Visual;

public sealed class VisualSceneSummaryTests
{
    [Fact]
    public void IsPhotoRelatedQuestion_matches_common_photo_queries()
    {
        VisualSceneSummary.IsPhotoRelatedQuestion("what am I doing in this photo?").Should().BeTrue();
        VisualSceneSummary.IsPhotoRelatedQuestion("what else am I doing in the last photo?").Should().BeTrue();
        VisualSceneSummary.IsPhotoRelatedQuestion("tell me about Raha").Should().BeFalse();
    }

    [Fact]
    public void TryParseFromExtractJson_reads_sceneSummary_field()
    {
        var json = """
                   {"sceneSummary":"Raha kayaking with a dog wearing sunglasses","memoryIntents":[]}
                   """;
        VisualSceneSummary.TryParseFromExtractJson(json)
            .Should()
            .Be("Raha kayaking with a dog wearing sunglasses");
    }

    [Fact]
    public void BuildFromIntents_joins_observation_values()
    {
        var summary = VisualSceneSummary.BuildFromIntents(new[]
        {
            new MemoryIntent { Value = "in a blue kayak" },
            new MemoryIntent { Value = "with a black dog" }
        });
        summary.Should().Contain("kayak");
        summary.Should().Contain("dog");
    }

    [Fact]
    public void HasSufficientSceneContext_true_when_scene_present()
    {
        var result = new PersonVisualContextResult(
            "appendix",
            new[]
            {
                new PersonVisualContextAsset
                {
                    AssetId = "a1",
                    SceneSummary = "Raha paddling a kayak on the lake with sunglasses"
                }
            });
        VisualSceneSummary.HasSufficientSceneContext(result).Should().BeTrue();
    }

    [Fact]
    public void ShouldUsePersonQueryVision_when_photo_question_and_no_scene()
    {
        var result = new PersonVisualContextResult(
            "appendix",
            new[] { new PersonVisualContextAsset { AssetId = "a1", Caption = "this is Raha" } });

        VisualSceneSummary.ShouldUsePersonQueryVision(
                "person-query",
                "what am I doing in this photo?",
                result,
                visionServicesAvailable: true)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ShouldUsePersonQueryVision_false_when_scene_already_stored()
    {
        var result = new PersonVisualContextResult(
            "appendix",
            new[]
            {
                new PersonVisualContextAsset
                {
                    AssetId = "a1",
                    SceneSummary = "Raha kayaking with a dog on calm water"
                }
            });

        VisualSceneSummary.ShouldUsePersonQueryVision(
                "person-query",
                "what am I doing in this photo?",
                result,
                visionServicesAvailable: true)
            .Should()
            .BeFalse();
    }
}
