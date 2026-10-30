using AgctorSDK.Core.SceneMemory;
using Xunit;

namespace AgctorSDK.Core.Tests;

public sealed class ScenePromptDiagnosticsTests
{
    [Theory]
    [InlineData("Raha says “Don’t go!”", "Raha says \"Don't go!\"", "Raha says “Don’t go!”")]
    [InlineData("Ryan\n  holds\t a sword.", "Ryan holds a sword.", "Ryan\n  holds\t a sword.")]
    [InlineData("Ryan holds a sword.", "Ryan carries a sword.", "Ryan carries a sword.")]
    [InlineData("Ryan does not drop it.", "Ryan drops it.", "Ryan drops it.")]
    [InlineData("Ryan  holds it. Ryan\nholds it.", "Ryan holds it.", "Ryan holds it.")]
    public void Quote_repair_only_restores_unique_typographical_matches(string prompt, string quote, string expected) =>
        Assert.Equal(expected, ScenePromptDiagnostics.RestoreSourceQuote(prompt, quote));

    [Fact]
    public void Reports_all_field_failures_with_indexes_and_only_verified_source_positions()
    {
        var prompt = "Raha raises a sword. Ryan watches.";
        var workspace = new SceneWorkspace("w", "Test", DateTimeOffset.UtcNow, new[] { new SceneEntity("r", "Raha", "Character") }, Array.Empty<SceneFact>());
        var extraction = new ScenePromptExtraction(Array.Empty<ScenePromptEntity>(), new[] {
            new ScenePromptChange("r", "Held By", "", "", false, "Raha raises a sword"),
            new ScenePromptChange("unknown", "action", null!, null!, false, "Raha lifts a sword") }, Array.Empty<string>(), new[] {
            new ScenePromptEvent("Ryan watches", new[] { "unknown" }, "Ryan watches", "planned") });
        var error = Assert.Throws<ScenePromptValidationException>(() => ScenePromptReducer.Apply(workspace,
            new("p", ScenePromptReducer.Revision(workspace), prompt, "", "test", extraction)));
        Assert.Contains(error.Issues, i => i.Path == "changes[0]" && i.Code == "invalid_property" && i.SourceStart == 0);
        Assert.Contains(error.Issues, i => i.Path == "changes[1]" && i.Code == "unknown_subject");
        Assert.Contains(error.Issues, i => i.Code == "invalid_value");
        Assert.Contains(error.Issues, i => i.Code == "missing_target");
        Assert.Contains(error.Issues, i => i.Code == "evidence_mismatch" && i.Evidence == "Raha lifts a sword" && i.SourceStart == null);
        Assert.Contains(error.Issues, i => i.Path == "events[0]" && i.Code == "invalid_event_mode");
        Assert.Contains(error.Issues, i => i.Code == "invalid_participants");
        Assert.Empty(workspace.History);
    }

    [Fact]
    public void Relationship_failures_identify_the_change_and_its_original_passage()
    {
        const string prompt = "The sun rises. Raha is trapped in slime.";
        var workspace = new SceneWorkspace("w", "Test", DateTimeOffset.UtcNow, Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        var plan = new ScenePromptExtraction(new[] { new ScenePromptEntity("r", "Raha", "Character"), new("s", "Slime", "Object") },
            new[] { new ScenePromptChange("r", "held_by", "", "s", false, "Raha is trapped in slime") }, Array.Empty<string>());
        var error = Assert.Throws<ScenePromptValidationException>(() => ScenePromptReducer.Apply(workspace,
            new("p", ScenePromptReducer.Revision(workspace), prompt, "", "test", plan)));
        var issue = Assert.Single(error.Issues);
        Assert.Equal("changes[0]", issue.Path);
        Assert.Equal("held_by", issue.Property);
        Assert.Equal("Raha is trapped in slime", prompt.Substring(issue.SourceStart!.Value, issue.SourceLength!.Value));
        Assert.Empty(workspace.Entities);
    }
}
