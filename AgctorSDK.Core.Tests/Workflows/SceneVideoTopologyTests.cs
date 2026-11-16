using AgctorSDK.Core.Workflows;
using Xunit;

namespace AgctorSDK.Core.Tests.Workflows;

public sealed class SceneVideoTopologyTests
{
    [Fact]
    public void Validator_accepts_fixed_roster_and_rejects_director_tool_edges()
    {
        var doc = Sample();
        Assert.Empty(SceneVideoTopologyValidator.Validate(doc));

        doc.Edges.Add(new SceneVideoTopologyEdge { Id = "bad", From = "Director", To = "Video rendering", Intent = "hack" });
        Assert.Contains(SceneVideoTopologyValidator.Validate(doc), e => e.Contains("Director must not own capability edge"));
    }

    [Fact]
    public void Validator_requires_agents_and_forbids_model_catalog_nodes()
    {
        var doc = Sample();
        doc.Nodes.RemoveAll(n => n.Id == "Planner");
        Assert.Contains(SceneVideoTopologyValidator.Validate(doc), e => e.Contains("Planner"));

        doc = Sample();
        doc.Nodes.Add(new SceneVideoTopologyNode { Id = "kokoro", Kind = "model", Detail = "nope" });
        Assert.Contains(SceneVideoTopologyValidator.Validate(doc), e => e.Contains("forbids model nodes"));
    }

    [Fact]
    public void Validator_rejects_layout_for_unknown_nodes()
    {
        var doc = Sample();
        doc.Ui = new SceneVideoTopologyUi
        {
            NodeLayouts = new Dictionary<string, SceneVideoTopologyLayout>
            {
                ["Director"] = new() { X = 10, Y = 20 },
                ["Ghost"] = new() { X = 1, Y = 2 }
            }
        };
        Assert.Contains(SceneVideoTopologyValidator.Validate(doc), e => e.Contains("Ghost"));
    }

    [Fact]
    public void Fixed_roster_edit_allows_audio_and_layout_only()
    {
        var baseline = Sample();
        var edited = Sample();
        edited.AudioEnabled = false;
        edited.Ui = new SceneVideoTopologyUi
        {
            NodeLayouts = new Dictionary<string, SceneVideoTopologyLayout>
            {
                ["Director"] = new() { X = 120, Y = 80 }
            }
        };
        Assert.Empty(SceneVideoTopologyValidator.ValidateFixedRosterEdit(baseline, edited));

        edited.Nodes.Add(new SceneVideoTopologyNode { Id = "Extra", Kind = "agent", Detail = "nope" });
        Assert.Contains(SceneVideoTopologyValidator.ValidateFixedRosterEdit(baseline, edited), e => e.Contains("cannot add or remove"));

        edited = Sample();
        edited.Edges.Add(new SceneVideoTopologyEdge { Id = "hack", From = "Director", To = "Video rendering", Intent = "hack" });
        Assert.Contains(SceneVideoTopologyValidator.ValidateFixedRosterEdit(baseline, edited), e =>
            e.Contains("Director must not own") || e.Contains("cannot change agent/tool edges"));
    }

    [Fact]
    public void Composer_unions_skeleton_with_observed_and_aliases_visual_critic()
    {
        var definition = Sample();
        var observed = new AgentRunGraph(
            [
                new("Continuity critic", "agent", null),
                new("Director", "agent", null),
                new("kokoro", "model", "Reported by Cynelo")
            ],
            [new("Director", "Continuity critic", "Review", "completed", 2, DateTimeOffset.UtcNow)],
            [new("ex1", "Director", "Continuity critic", "Review continuity", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), "agent", "agent")]);

        var composed = SceneVideoGraphComposer.Compose(definition, observed);
        Assert.Contains(composed.Nodes, n => n.Id == "Visual critic" && n.Kind == "agent");
        Assert.DoesNotContain(composed.Nodes, n => n.Id == "Continuity critic");
        Assert.Contains(composed.Nodes, n => n.Id == "Director");
        Assert.Contains(composed.Nodes, n => n.Id == "Audio Engineer");
        Assert.Contains(composed.Nodes, n => n.Id == "Video rendering" && n.Kind == "tool");
        Assert.Contains(composed.Nodes, n => n.Id == "kokoro" && n.Kind == "model");
        Assert.Contains(composed.Edges, e => e.From == "Director" && e.To == "Visual critic" && e.Count == 2);
        Assert.Equal("Visual critic", Assert.Single(composed.ActiveCalls!).To);
    }

    private static SceneVideoTopologyDocument Sample() => new()
    {
        SchemaVersion = "1.0",
        GraphId = "scene-video-chain",
        ModelEvidencePolicy = "observed-only",
        AudioEnabled = true,
        Nodes =
        [
            new() { Id = "Human", Kind = "human", Detail = "human" },
            new() { Id = "Director", Kind = "agent", Detail = "Actor mailbox" },
            new() { Id = "Audio Engineer", Kind = "agent", Detail = "Actor mailbox" },
            new() { Id = "Visual critic", Kind = "agent", Detail = "Actor mailbox", Aliases = ["Continuity critic"] },
            new() { Id = "Planner", Kind = "agent", Detail = "Actor mailbox" },
            new() { Id = "Project agent", Kind = "agent", Detail = "Actor mailbox" },
            new() { Id = "Render agent", Kind = "agent", Detail = "Actor mailbox" },
            new() { Id = "Scene Memory agent", Kind = "agent", Detail = "Actor mailbox" },
            new() { Id = "Editor agent", Kind = "agent", Detail = "Actor mailbox" },
            new() { Id = "Ollama", Kind = "llm", Detail = "gemma4:latest", OwnerAgentId = "Planner" },
            new() { Id = "Scene Memory", Kind = "memory", Detail = "Recorded facts", OwnerAgentId = "Scene Memory agent" },
            new() { Id = "Video rendering", Kind = "tool", Detail = "Cynelo API", OwnerAgentId = "Render agent" },
            new() { Id = "Audio production", Kind = "tool", Detail = "Cynelo API", OwnerAgentId = "Audio Engineer" }
        ],
        Edges =
        [
            new() { Id = "d-p", From = "Director", To = "Planner", Intent = "Plan" },
            new() { Id = "d-v", From = "Director", To = "Visual critic", Intent = "Review" },
            new() { Id = "r-v", From = "Render agent", To = "Video rendering", Intent = "Submit" },
            new() { Id = "a-a", From = "Audio Engineer", To = "Audio production", Intent = "Mix" }
        ]
    };
}
