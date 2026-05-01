using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Host.Services.ProjectMemory;
using FluentAssertions;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>Unit-style tests for playground trace JSON shapes (Host assembly, internal helper).</summary>
public sealed class PlaygroundTraceTimelineDetailTests
{
    [Fact]
    public void BuildIngestJson_includes_extractor_preview_and_truncation_flag_when_long()
    {
        var longOut = new string('x', PlaygroundTraceTimelineDetail.MaxIngestExtractorPreviewChars + 50);
        var ingest = new ProjectMemoryIngestResult
        {
            ParseSuccess = true,
            ParseSource = "actionIntents.memory.persist",
            WroteAnyFile = false,
            Summary = "ok",
            UpdatedFiles = []
        };

        var json = PlaygroundTraceTimelineDetail.BuildIngestJson("people", ingest, longOut);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        r.GetProperty("kind").GetString().Should().Be("pm.playground.ingest-disk");
        r.GetProperty("parseSource").GetString().Should().Be("actionIntents.memory.persist");
        r.GetProperty("extractorOutputChars").GetInt32().Should().Be(longOut.Length);
        r.GetProperty("extractorOutputTruncated").GetBoolean().Should().BeTrue();
        r.GetProperty("extractorOutputPreview").GetString()!.Length.Should().Be(PlaygroundTraceTimelineDetail.MaxIngestExtractorPreviewChars);
    }

    [Fact]
    public void BuildIngestJson_includes_out_of_schema_proposals_when_present()
    {
        var ingest = new ProjectMemoryIngestResult
        {
            ParseSuccess = true,
            WroteAnyFile = true,
            Summary = "ok",
            UpdatedFiles = ["people/a/profile.md"],
            OutOfSchemaProposals =
            [
                new OutOfSchemaFactProposal
                {
                    ProposalId = "abc",
                    EntityKey = "raha",
                    KnowledgeType = "pets",
                    Attribute = "dogs",
                    Value = "two",
                    Confidence = 0.9,
                    Disposition = OutOfSchemaDisposition.ImmediateConfirmation,
                    UserPromptLine = "Ask user?"
                }
            ]
        };

        var json = PlaygroundTraceTimelineDetail.BuildIngestJson("s1", ingest, null);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        r.GetProperty("outOfSchemaTruncated").GetBoolean().Should().BeFalse();
        var arr = r.GetProperty("outOfSchemaProposals");
        arr.GetArrayLength().Should().Be(1);
        arr[0].GetProperty("proposalId").GetString().Should().Be("abc");
    }

    [Fact]
    public void BuildIngestJson_omits_preview_fields_when_no_extractor_output()
    {
        var ingest = new ProjectMemoryIngestResult
        {
            ParseSuccess = false,
            WroteAnyFile = false,
            Summary = "parse fail",
            UpdatedFiles = []
        };

        var json = PlaygroundTraceTimelineDetail.BuildIngestJson("people", ingest, null);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        r.TryGetProperty("extractorOutputChars", out _).Should().BeFalse();
        r.TryGetProperty("extractorOutputPreview", out _).Should().BeFalse();
        r.GetProperty("extractorOutputTruncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void BuildStreamRootJson_includes_mode_status_and_persona_chain()
    {
        var json = PlaygroundTraceTimelineDetail.BuildStreamRootJson(
            sessionId: "s1",
            messageId: "m1",
            scenarioId: "people",
            selectedAgentId: "memory-curator",
            usedScenarioFlow: true,
            status: "success",
            personaChain: new[] { "person-extractor", "memory-curator" },
            responseChars: 321,
            ingestAttempted: true);

        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        r.GetProperty("kind").GetString().Should().Be("pm.playground.stream-root");
        r.GetProperty("usedScenarioFlow").GetBoolean().Should().BeTrue();
        r.GetProperty("status").GetString().Should().Be("success");
        r.GetProperty("responseChars").GetInt32().Should().Be(321);
        r.GetProperty("ingestAttempted").GetBoolean().Should().BeTrue();
        r.GetProperty("personaChain").EnumerateArray().Select(x => x.GetString()).Should().ContainInOrder("person-extractor", "memory-curator");
    }
}
