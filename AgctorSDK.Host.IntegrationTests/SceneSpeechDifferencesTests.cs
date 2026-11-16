using AgctorSDK.Host.Services.VideoWorkflow;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SceneSpeechDifferencesTests
{
    [Fact]
    public void Missing_negation_is_reported_without_flagging_correct_words()
    {
        var issue = Assert.Single(SceneSpeechDifferences.Find("Do not go.", "Do go."));
        Assert.Equal(0, issue.Length);
        Assert.Equal("Missing expected words: not", issue.Reason);
    }
    [Fact]
    public void Separate_changes_and_repeated_words_keep_precise_offsets()
    {
        const string transcript = "😀 Go left then go slowly.";
        var issues = SceneSpeechDifferences.Find("Go right then go quickly.", transcript);
        Assert.Equal(new[] { "left", "slowly" }, issues.Select(i => transcript.Substring(i.Start, i.Length)));
    }
    [Theory]
    [InlineData("Don't GO!", "Don’t go.")]
    [InlineData("سلام دوست من", "سلام، دوست من!")]
    public void Punctuation_and_case_are_not_word_problems(string expected, string transcript) =>
        Assert.Empty(SceneSpeechDifferences.Find(expected, transcript));
    [Fact]
    public void Extra_and_missing_tail_words_are_distinguished()
    {
        var extra = Assert.Single(SceneSpeechDifferences.Find("Hello", "Hello friend"));
        Assert.Equal(6, extra.Start); Assert.Equal(6, extra.Length);
        var missing = Assert.Single(SceneSpeechDifferences.Find("Hello friend", "Hello"));
        Assert.Equal(5, missing.Start); Assert.Equal(0, missing.Length);
    }
}
