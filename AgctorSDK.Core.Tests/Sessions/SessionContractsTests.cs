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
                TurnGroupId = "turn-group-1",
                Sequence = 2,
                Role = SessionRole.User,
                Content = "add it to MathUtils",
                AgentId = "coder-agent"
            };

            turn.TurnId.Should().NotBeNullOrWhiteSpace();
            turn.TurnGroupId.Should().Be("turn-group-1");
            turn.SessionId.Should().Be("session-1");
            turn.Sequence.Should().Be(2);
            turn.Role.Should().Be(SessionRole.User);
            turn.Content.Should().Contain("MathUtils");
            turn.AgentId.Should().Be("coder-agent");
        }

        [Fact]
        public void SessionTraceLink_CanLink_MessageAndTurnTraceIds()
        {
            var link = new SessionTraceLink
            {
                SessionId = "session-1",
                TurnGroupId = "turn-group-1",
                RequestTurnId = "user-turn-1",
                ResponseTurnId = "assistant-turn-1",
                PrimaryTraceId = "trace-primary",
                RequestTraceId = "trace-request",
                ResponseTraceId = "trace-response",
                AgentId = "query-agent"
            };

            link.TraceLinkId.Should().NotBeNullOrWhiteSpace();
            link.TurnGroupId.Should().Be("turn-group-1");
            link.RequestTurnId.Should().Be("user-turn-1");
            link.ResponseTurnId.Should().Be("assistant-turn-1");
            link.PrimaryTraceId.Should().Be("trace-primary");
            link.RequestTraceId.Should().Be("trace-request");
            link.ResponseTraceId.Should().Be("trace-response");
            link.AgentId.Should().Be("query-agent");
        }
    }
}
