using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Decisions;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Decisions.Providers;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.IntegrationTests.Decisions
{
    /// <summary>
    /// Actor -> decision actor -> real provider adapters. HTTP is stubbed so no API key is required.
    /// </summary>
    [TestClass]
    public class DecisionFabricActorIntegrationTests
    {
        [TestMethod]
        public async Task ActorDecide_EscalatesLowLayaConfidence_ToOpenAi()
        {
            var handler = new RecordingHandler();
            var http = new HttpClient(handler);
            var options = new DecisionFabricOptions();
            options.Routing.ConfidenceThreshold = 0.70;
            var laya = new LayaDecisionProvider(http, options.Providers.Laya);
            var llm = new OpenAiDecisionProvider(http, options.Providers.OpenAI);
            var providers = new IDecisionProvider[] { new RuleDecisionProvider(), laya, llm };
            var events = new List<DecisionEvent>();
            var service = new DecisionService(
                new DecisionRouter(providers, options),
                options,
                new DecisionTelemetry(new[] { new ListListener(events) }));
            var runtime = new InMemoryActorRuntime(LoggerFactory.CreateLogger("decision-integration"), service);
            await runtime.InitializeAsync(new Dictionary<string, object>());

            var actor = await runtime.SpawnActorAsync("sales", id => new SalesActor(id));
            var decision = await actor.Context!.Decide(
                Choice.Create("What action should I take?", "call", "email", "wait"));

            Assert.AreEqual("call", decision.Value);
            Assert.AreEqual(DecisionFabricIds.OpenAI, decision.Provider);
            Assert.AreEqual(DecisionFabricIds.Laya, decision.PreviousProvider);
            Assert.IsTrue(decision.Escalated);
            Assert.AreEqual(0.91, decision.Confidence, 0.001);
            Assert.IsTrue(decision.Latency >= TimeSpan.Zero);
            Assert.AreEqual(2, handler.Bodies.Count);
            StringAssert.Contains(handler.Bodies[0], "\"type\":\"choice\"");
            StringAssert.Contains(handler.Bodies[1], "chat/completions");
            Assert.IsTrue(events.Exists(e => e.Kind == DecisionEventKind.DecisionRequested));
            Assert.IsTrue(events.Exists(e => e.Kind == DecisionEventKind.DecisionEscalated));
            Assert.IsTrue(events.Exists(e => e.Kind == DecisionEventKind.DecisionCompleted));
        }

        [TestMethod]
        public async Task ActorMessage_UsesProtectedDecide()
        {
            var handler = new RecordingHandler();
            var http = new HttpClient(handler);
            var options = new DecisionFabricOptions();
            var providers = new IDecisionProvider[]
            {
                new RuleDecisionProvider(),
                new LayaDecisionProvider(http, options.Providers.Laya),
                new OpenAiDecisionProvider(http, options.Providers.OpenAI)
            };
            var service = new DecisionService(new DecisionRouter(providers, options), options, new DecisionTelemetry());
            var runtime = new InMemoryActorRuntime(LoggerFactory.CreateLogger("decision-integration"), service);
            await runtime.InitializeAsync(new Dictionary<string, object>());
            await runtime.SpawnActorAsync("sales-msg", id => new SalesActor(id));

            var result = await runtime.SendMessageAsync<DecisionResult>("sales-msg", "tick", TimeSpan.FromSeconds(5));

            Assert.AreEqual("call", result.Value);
            Assert.IsTrue(result.Escalated);
        }

        private sealed class SalesActor : Actor
        {
            public SalesActor(string id) : base(id)
            {
            }

            public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
            {
                var decision = await Decide(Choice.Create("What action should I take?", "call", "email", "wait"), cancellationToken);
                return new MessageEnvelope(decision);
            }
        }

        private sealed class ListListener : IDecisionEventListener
        {
            private readonly List<DecisionEvent> _events;
            public ListListener(List<DecisionEvent> events) => _events = events;
            public void OnEvent(DecisionEvent decisionEvent) => _events.Add(decisionEvent);
        }

        /// <summary>
        /// Laya answers with low confidence. The OpenAI-shaped stub answers with the final choice.
        /// </summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            public List<string> Bodies { get; } = new List<string>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                var uri = request.RequestUri?.ToString() ?? string.Empty;
                Bodies.Add(uri + "\n" + body);

                string json;
                if (uri.Contains("chat/completions", StringComparison.Ordinal))
                {
                    json = """
                        {"choices":[{"message":{"role":"assistant","content":"{\"value\":\"call\",\"confidence\":0.91,\"scores\":{\"call\":0.91,\"email\":0.05,\"wait\":0.04},\"summary\":\"Call while the lead is warm\"}"}}]}
                        """;
                }
                else
                {
                    json = """
                        {"selected":"email","confidence":0.43,"scores":{"call":0.20,"email":0.43,"wait":0.37},"summary":"weak preference","cost":0}
                        """;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
