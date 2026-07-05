using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Sessions.Models;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Sessions;

public class SessionTranscriptFormatterTests
{
    [Fact]
    public void TakeRecentTurns_KeepsLastN_BySequence()
    {
        var turns = Enumerable.Range(1, 30)
            .Select(i => new SessionTurn
            {
                Sequence = i,
                Role = SessionRole.User,
                Content = $"msg-{i}"
            })
            .ToList();

        var recent = SessionTranscriptFormatter.TakeRecentTurns(turns, 25);

        recent.Should().HaveCount(25);
        recent.Select(t => t.Sequence).Should().BeEquivalentTo(Enumerable.Range(6, 25));
    }

    [Fact]
    public void BuildPrefix_WithMaxTurns_IncludesOnlyRecentLines()
    {
        var turns = Enumerable.Range(1, 30)
            .Select(i => new SessionTurn
            {
                Sequence = i,
                Role = SessionRole.User,
                Content = $"line-{i}"
            })
            .ToList();

        var prefix = SessionTranscriptFormatter.BuildPrefix(turns, maxTurns: 25);

        prefix.Should().Contain("line-6");
        prefix.Should().Contain("line-30");
        prefix.Should().NotContain("line-5");
    }

    [Fact]
    public void ForPromptContext_ExcludesTurnGroup_ThenCaps()
    {
        var turns = new List<SessionTurn>
        {
            new() { Sequence = 1, TurnGroupId = "g1", Role = SessionRole.User, Content = "first" },
            new() { Sequence = 2, TurnGroupId = "g2", Role = SessionRole.User, Content = "current" }
        };

        var ctx = SessionTranscriptFormatter.ForPromptContext(turns, "g2");

        ctx.Should().HaveCount(1);
        ctx[0].Content.Should().Be("first");
    }

    [Fact]
    public void ExpandFollowUpFromHistory_MapsTryAgain_ToLastQuestion()
    {
        var prior = new List<SessionTurn>
        {
            new() { Sequence = 1, Role = SessionRole.User, Content = "What is Ryan's relation to Raha?" },
            new() { Sequence = 2, Role = SessionRole.Assistant, Content = "Error: timeout" }
        };

        SessionTranscriptFormatter.ExpandFollowUpFromHistory("try again", prior)
            .Should().Be("What is Ryan's relation to Raha?");
    }

    [Fact]
    public void ExpandFollowUpFromHistory_LeavesNormalMessages_Unchanged()
    {
        SessionTranscriptFormatter.ExpandFollowUpFromHistory("Who is Ryan?", Array.Empty<SessionTurn>())
            .Should().Be("Who is Ryan?");
    }
}
