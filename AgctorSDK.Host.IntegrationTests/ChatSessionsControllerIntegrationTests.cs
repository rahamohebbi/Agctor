using System.Net;
using System.Net.Http.Json;
using System.Linq;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests
{
    public class ChatSessionsControllerIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
    {
        private readonly HttpClient _client;
        private static int _portCounter = 12080;

        public ChatSessionsControllerIntegrationTests(AgctorWebApplicationFactory factory)
        {
            var configured = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var uniquePort = Interlocked.Increment(ref _portCounter);
                    config.AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString())
                    });
                });
            });
            _client = configured.CreateClient();
        }

        [Fact]
        public async Task CreateListAndGetSession_Works()
        {
            var createResponse = await _client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest
            {
                Title = "Integration Session"
            });

            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await createResponse.Content.ReadFromJsonAsync<SessionInfo>();
            created.Should().NotBeNull();
            created!.SessionId.Should().NotBeNullOrWhiteSpace();

            var listResponse = await _client.GetAsync("/api/chat/sessions?limit=20");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var list = await listResponse.Content.ReadFromJsonAsync<List<SessionInfo>>();
            list.Should().NotBeNull();
            list!.Any(s => s.SessionId == created.SessionId).Should().BeTrue();

            var getResponse = await _client.GetAsync($"/api/chat/sessions/{created.SessionId}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var transcript = await getResponse.Content.ReadFromJsonAsync<SessionTranscript>();
            transcript.Should().NotBeNull();
            transcript!.Session.SessionId.Should().Be(created.SessionId);
        }

        [Fact]
        public async Task AgentMessage_WithSessionId_AppendsUserTurn()
        {
            var createResponse = await _client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest());
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await createResponse.Content.ReadFromJsonAsync<SessionInfo>();
            created.Should().NotBeNull();

            // Agent is expected to be missing in default startup. We only verify session turn persistence.
            var sendResponse = await _client.PostAsJsonAsync("/api/agents/non-existent-agent/message", new MessageRequest
            {
                Payload = "remember this turn",
                SessionId = created!.SessionId
            });
            sendResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var sessionResponse = await _client.GetAsync($"/api/chat/sessions/{created.SessionId}");
            sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var transcript = await sessionResponse.Content.ReadFromJsonAsync<SessionTranscript>();
            transcript.Should().NotBeNull();
            transcript!.Turns.Count.Should().BeGreaterThan(0);
            transcript.Turns.Last().Role.Should().Be(SessionRole.User);
            transcript.Turns.Last().Content.Should().Contain("remember this turn");
            transcript.Turns.Last().TurnGroupId.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task AgentMessage_WithSessionId_CapturesTraceHistory()
        {
            var createResponse = await _client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest());
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var created = await createResponse.Content.ReadFromJsonAsync<SessionInfo>();
            created.Should().NotBeNull();

            var sendResponse = await _client.PostAsJsonAsync("/api/agents/non-existent-agent/message", new MessageRequest
            {
                Payload = "trace this request",
                SessionId = created!.SessionId
            });
            sendResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var transcriptResponse = await _client.GetAsync($"/api/chat/sessions/{created.SessionId}");
            transcriptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var transcript = await transcriptResponse.Content.ReadFromJsonAsync<SessionTranscript>();
            transcript.Should().NotBeNull();
            transcript!.TraceLinks.Should().HaveCount(1);

            var traceLink = transcript.TraceLinks.Single();
            traceLink.RequestTurnId.Should().Be(transcript.Turns.Single().TurnId);
            traceLink.PrimaryTraceId.Should().NotBeNullOrWhiteSpace();

            var timelineResponse = await _client.GetAsync($"/api/Visualization/sessions/{created.SessionId}/messages/{traceLink.RequestTurnId}/timeline");
            timelineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var timeline = await timelineResponse.Content.ReadFromJsonAsync<TraceTimelineResponse>();
            timeline.Should().NotBeNull();
            timeline!.TraceId.Should().Be(traceLink.PrimaryTraceId);
            timeline.Events.Should().NotBeEmpty();
        }
    }
}
