using System.Text.Json;
using AgctorSDK.Core.Workflows;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Host.Services.VideoWorkflow;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>Phase E: admission flow → topology.audioEnabled seeding rules.</summary>
public sealed class SceneVideoTopologyMigrationTests
{
    [Fact]
    public void Derive_audio_from_four_node_admission_with_audio_engineer()
    {
        var flow = Admission(withAudio: true);
        Assert.True(SceneVideoTopologyMigration.DeriveAudioEnabledFromAdmission(flow));
    }

    [Fact]
    public void Derive_audio_false_from_three_node_admission()
    {
        var flow = Admission(withAudio: false);
        Assert.False(SceneVideoTopologyMigration.DeriveAudioEnabledFromAdmission(flow));
    }

    [Fact]
    public void Seed_updates_document_only_when_admission_differs()
    {
        var doc = new SceneVideoTopologyDocument { AudioEnabled = true };
        Assert.False(SceneVideoTopologyMigration.TrySeedAudioFromAdmission(doc, Admission(withAudio: true), out _));
        Assert.True(doc.AudioEnabled);

        Assert.True(SceneVideoTopologyMigration.TrySeedAudioFromAdmission(doc, Admission(withAudio: false), out var audio));
        Assert.False(audio);
        Assert.False(doc.AudioEnabled);
    }

    [Fact]
    public void Derive_returns_null_for_non_scene_video_flow()
    {
        var flow = new ScenarioFlowDocument
        {
            Nodes =
            [
                new() { Id = "in", Type = "ChatInput" },
                new() { Id = "llm", Type = "LlmNode" },
                new() { Id = "out", Type = "Output" }
            ]
        };
        Assert.Null(SceneVideoTopologyMigration.DeriveAudioEnabledFromAdmission(flow));
        Assert.Null(SceneVideoTopologyMigration.DeriveAudioEnabledFromAdmission(null));
    }

    private static ScenarioFlowDocument Admission(bool withAudio)
    {
        var nodes = new List<ScenarioFlowNode>
        {
            new() { Id = "input", Type = "ChatInput", Label = "Your idea" },
            new()
            {
                Id = "director",
                Type = "AgentLoop",
                Label = "Director",
                Config = JsonSerializer.SerializeToElement(new { workflow = "scene-video", maxAttempts = 3 })
            },
            new() { Id = "output", Type = "Output", Label = "Finished" }
        };
        if (withAudio)
        {
            nodes.Insert(2, new ScenarioFlowNode
            {
                Id = "audio-engineer",
                Type = "AgentLoop",
                Label = "Audio Engineer",
                Config = JsonSerializer.SerializeToElement(new { workflow = "scene-video", role = "audio-engineer" })
            });
        }
        return new ScenarioFlowDocument { Nodes = nodes, Edges = [] };
    }
}
