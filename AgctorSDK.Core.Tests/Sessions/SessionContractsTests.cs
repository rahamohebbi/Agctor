using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Sessions.Models;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Sessions
{
    public class SessionContractsTests
    {
        [Fact]
        public void SessionMemoryOptions_HasReasonableDefaults()
        {
            var options = new SessionMemoryOptions();

            options.RecentTurnWindow.Should().BeGreaterThan(0);
            options.SummaryRefreshTurns.Should().BeGreaterThan(0);
            options.MaxContextChars.Should().BeGreaterThan(0);
        }

        [Fact]
        public void SessionTurn_CanHoldSessionData()
        {
            var turn = new SessionTurn
            {
                SessionId = "session-1",
                Sequence = 2,
                Role = SessionRole.User,
                Content = "add it to MathUtils",
                AgentId = "coder-agent"
            };

            turn.TurnId.Should().NotBeNullOrWhiteSpace();
            turn.SessionId.Should().Be("session-1");
            turn.Sequence.Should().Be(2);
            turn.Role.Should().Be(SessionRole.User);
            turn.Content.Should().Contain("MathUtils");
            turn.AgentId.Should().Be("coder-agent");
        }
    }
}
