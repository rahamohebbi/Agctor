using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.CodeGraph.Intents;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.Core.Messages;
using Xunit;

namespace AgctorSDK.CodeGraph.Tests.Agents
{
    public class IntentDetectionAgentTests
    {
        private sealed class StubClient : ILlmClient
        {
            private readonly string _json;
            public StubClient(string json) => _json = json;
            public Task<string> CompleteAsync(string prompt, LlmOptions? options = null) => Task.FromResult(_json);
        }

        [Fact]
        public async Task ShouldReturnStructuredIntent()
        {
            // arrange – JSON reply for list classes
            var json = "{\"intent\": \"list_classes\", \"slots\": {} }";
            var client = new StubClient(json);
            var agent = new IntentDetectionAgent("intent-agent", client);
            await agent.InitializeAsync();
            // act
            var envelope = new MessageEnvelope(new InterpretQueryMessage("list classes"));
            var resp = await agent.ReceiveAsync(envelope);
            // assert
            var payload = Assert.IsType<IntentResolvedMessage>(resp.Payload);
            Assert.True(payload.Resolution.IsSuccess);
            Assert.Equal(IntentKind.ListClasses, payload.Resolution.Kind);
        }
    }
} 