using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ScenarioFlowRouterLlmParserTests
{
    [Fact]
    public void Parse_StripsMarkdownFencesAndExtractsTargets()
    {
        var raw = """
            ```json
            { "schemaVersion": "1.0", "targets": [ { "personaId": "a" }, { "personaId": "evil" } ], "needsClarification": false }
            ```
            """;
        var r = ScenarioFlowRouterLlmParser.Parse(raw, new[] { "a", "b" }, ScenarioFlowRouterConfig.Default);
        r.Ok.Should().BeTrue();
        r.SelectedPersonaIds.Should().Equal("a");
    }

    [Fact]
    public void Parse_MinConfidenceFilters()
    {
        var raw = """{"schemaVersion":"1.0","targets":[{"personaId":"x","confidence":0.2},{"personaId":"y","confidence":0.9}],"needsClarification":false}""";
        var cfg = new ScenarioFlowRouterConfig(ScenarioFlowRouterMode.Llm, null, 0.5, null, null);
        var r = ScenarioFlowRouterLlmParser.Parse(raw, new[] { "x", "y" }, cfg);
        r.Ok.Should().BeTrue();
        r.SelectedPersonaIds.Should().Equal("y");
    }

    [Fact]
    public void Parse_MaxTargetsCaps()
    {
        var raw = """{"schemaVersion":"1.0","targets":[{"personaId":"a"},{"personaId":"b"},{"personaId":"c"}],"needsClarification":false}""";
        var cfg = new ScenarioFlowRouterConfig(ScenarioFlowRouterMode.Llm, 2, null, null, null);
        var r = ScenarioFlowRouterLlmParser.Parse(raw, new[] { "a", "b", "c" }, cfg);
        r.Ok.Should().BeTrue();
        r.SelectedPersonaIds.Should().Equal("a", "b");
    }

    [Fact]
    public void Parse_FallbackWhenEmptyTargets()
    {
        var raw = """{"schemaVersion":"1.0","targets":[],"needsClarification":false}""";
        var cfg = new ScenarioFlowRouterConfig(ScenarioFlowRouterMode.Llm, null, null, "a", null);
        var r = ScenarioFlowRouterLlmParser.Parse(raw, new[] { "a" }, cfg);
        r.Ok.Should().BeTrue();
        r.SelectedPersonaIds.Should().Equal("a");
    }

    [Fact]
    public void Parse_ClarificationShortCircuitsTargets()
    {
        var raw = """
            {"schemaVersion":"1.0","targets":[{"personaId":"a"}],"needsClarification":true,"clarificationPrompt":"Which API?"}
            """;
        var r = ScenarioFlowRouterLlmParser.Parse(raw, new[] { "a" }, ScenarioFlowRouterConfig.Default);
        r.NeedsClarification.Should().BeTrue();
        r.ClarificationPrompt.Should().Be("Which API?");
    }
}
