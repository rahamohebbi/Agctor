using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Coref;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class FocusEntityPolicyTests
{
    [Theory]
    [InlineData("user", true)]
    [InlineData("unknown", true)]
    [InlineData("raha", false)]
    public void IsPlaceholderSlug_classifies_generic_slugs(string slug, bool expected) =>
        FocusEntityPolicy.IsPlaceholderSlug(slug).Should().Be(expected);

    [Fact]
    public void TryInferFromProjectName_matches_display_name()
    {
        var entities = new List<(string, string)> { ("raha", "Raha Mohebbi"), ("ryan", "Ryan") };
        var hit = FocusEntityPolicy.TryInferFromProjectName("Raha", entities);
        hit.Should().NotBeNull();
        hit!.Value.EntityKey.Should().Be("raha");
    }

    [Fact]
    public void CoalesceActiveSubject_prefers_real_candidate_over_placeholder()
    {
        FocusEntityPolicy.CoalesceActiveSubject("user", "raha").Should().Be("raha");
        FocusEntityPolicy.CoalesceActiveSubject("ryan", "raha").Should().Be("ryan");
    }

    [Fact]
    public void ResolveFocusFromExtract_skips_placeholder_entity_keys()
    {
        const string json = """{"memoryIntents":[{"entityKey":"user","knowledgeType":"observation","value":"gym","confidence":0.9}]}""";
        var (slug, source) = ProjectMemoryCoreferenceCoordinator.ResolveFocusFromExtract(json, "raha");
        slug.Should().Be("raha");
        source.Should().Be("resolved");
    }

    [Fact]
    public void TryRewritePlaceholderEntityKeys_remaps_user_to_focus()
    {
        const string json = """{"memoryIntents":[{"entityKey":"user","knowledgeType":"observation","value":"gym","confidence":0.9}]}""";
        MemoryIntentJson.TryRewritePlaceholderEntityKeys(json, "raha", out var rewritten).Should().BeTrue();
        rewritten.Should().Contain("\"entityKey\":\"raha\"");
    }

    [Fact]
    public void TryMatchPrimaryEntityInMessage_prefers_earliest_name()
    {
        var entities = new List<(string, string)> { ("raha", "Raha Mohebbi"), ("ryan", "Ryan") };
        var hit = FocusEntityPolicy.TryMatchPrimaryEntityInMessage("Ryan is Raha's son.", entities);
        hit.Should().NotBeNull();
        hit!.Value.EntityKey.Should().Be("ryan");
    }

    [Fact]
    public void ResolveFocusFromExtract_prefers_coreference_over_first_intent()
    {
        const string json =
            """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"relationship","value":"mother","confidence":0.9}]}""";
        var (slug, source) = ProjectMemoryCoreferenceCoordinator.ResolveFocusFromExtract(json, "ryan");
        slug.Should().Be("ryan");
        source.Should().Be("resolved");
    }
}
