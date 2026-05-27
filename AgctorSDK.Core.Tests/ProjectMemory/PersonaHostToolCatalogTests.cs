using AgctorSDK.Core.ProjectMemory.Scenarios;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.ProjectMemory;

public sealed class PersonaHostToolCatalogTests
{
    [Fact]
    public void ForPersona_person_query_includes_memory_and_visual_context()
    {
        var ids = PersonaHostToolCatalog.ForPersona("person-query").Select(d => d.Id).ToList();
        ids.Should().Contain(ScenarioFlowLlmNodeToolIds.PersonMemoryContext);
        ids.Should().Contain(ScenarioFlowLlmNodeToolIds.PersonVisualContext);
        ids.Should().NotContain(ScenarioFlowLlmNodeToolIds.ApplyMemoryIntents);
    }

    [Fact]
    public void ForPersona_memory_curator_includes_apply_intents_only()
    {
        var ids = PersonaHostToolCatalog.ForPersona("memory-curator").Select(d => d.Id).ToList();
        ids.Should().Contain(ScenarioFlowLlmNodeToolIds.ApplyMemoryIntents);
        ids.Should().NotContain(ScenarioFlowLlmNodeToolIds.PersonMemoryContext);
    }

    [Fact]
    public void ForPersona_visual_intake_includes_ingest_and_extract()
    {
        var ids = PersonaHostToolCatalog.ForPersona("visual-intake").Select(d => d.Id).ToList();
        ids.Should().Contain(ScenarioFlowLlmNodeToolIds.PersonVisualIngest);
        ids.Should().Contain(ScenarioFlowLlmNodeToolIds.PersonVisualExtract);
    }

    [Fact]
    public void ForPersona_unknown_returns_empty()
    {
        PersonaHostToolCatalog.ForPersona("not-a-persona").Should().BeEmpty();
    }
}
