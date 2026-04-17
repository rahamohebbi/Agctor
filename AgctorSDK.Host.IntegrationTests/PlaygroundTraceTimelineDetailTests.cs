using System.Text.Json;
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
            WroteAnyFile = false,
            Summary = "ok",
            UpdatedFiles = []
        };

        var json = PlaygroundTraceTimelineDetail.BuildIngestJson("people", ingest, longOut);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        r.GetProperty("kind").GetString().Should().Be("pm.playground.ingest-disk");
        r.GetProperty("extractorOutputChars").GetInt32().Should().Be(longOut.Length);
        r.GetProperty("extractorOutputTruncated").GetBoolean().Should().BeTrue();
        r.GetProperty("extractorOutputPreview").GetString()!.Length.Should().Be(PlaygroundTraceTimelineDetail.MaxIngestExtractorPreviewChars);
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
}
