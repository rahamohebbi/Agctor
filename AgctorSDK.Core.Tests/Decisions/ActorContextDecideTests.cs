using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Decisions;
using AgctorSDK.Core.Decisions.Providers;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Decisions
{
    [TestClass]
    public class ActorContextDecideTests
    {
        [TestMethod]
        public async Task ContextDecide_RoutesThroughDecisionActor()
        {
            var laya = new ScriptedProvider("laya", DecisionEngineRole.Laya, true, 40, 0m, 0.8, _ =>
                new DecisionResult { Value = "call", Confidence = 0.86, Cost = 0m, ReasoningSummary = "Call" });
            var runtime = await StartRuntime(laya);

            var actor = await runtime.SpawnActorAsync("sales-1", id => new SalesActor(id));
            var decision = await actor.Context!.Decide(
                Choice.Create("What action should I take?", "call", "email", "wait"));

            Assert.AreEqual("call", decision.Value);
            Assert.AreEqual(DecisionFabricIds.Laya, decision.Provider);
            Assert.AreEqual(0.86, decision.Confidence, 0.001);
            Assert.IsFalse(decision.Escalated);
            Assert.IsTrue(decision.Latency >= System.TimeSpan.Zero);
            Assert.AreEqual(1, laya.Calls);
            Assert.IsNotNull(await runtime.GetActorAsync<IActor>(DecisionFabricIds.DecisionActorId));
        }

        [TestMethod]
        public async Task ReceiveAsync_Decide_ReturnsResultToCaller()
        {
            var laya = new ScriptedProvider("laya", DecisionEngineRole.Laya, true, 40, 0m, 0.8, _ =>
                new DecisionResult { Value = "email", Confidence = 0.8, Cost = 0m });
            var runtime = await StartRuntime(laya);
            await runtime.SpawnActorAsync("sales-2", id => new SalesActor(id));

            var result = await runtime.SendMessageAsync<DecisionResult>(
                "sales-2",
                "tick",
                System.TimeSpan.FromSeconds(5));

            Assert.AreEqual("email", result.Value);
            Assert.AreEqual(DecisionFabricIds.Laya, result.Provider);
        }

        [TestMethod]
        public async Task DeterministicChoice_StaysOnRulesInsideActor()
        {
            var laya = new ScriptedProvider("laya", DecisionEngineRole.Laya, true, 40, 0m, 0.8, _ =>
                new DecisionResult { Value = "drop", Confidence = 0.99, Cost = 0m });
            var runtime = await StartRuntime(laya);
            var actor = await runtime.SpawnActorAsync("sales-3", id => new SalesActor(id));

            var request = Choice.Create("What should the sales agent do next?", "call", "email", "wait", "drop");
            request.State = new Dictionary<string, object> { ["status"] = "hot" };
            request.Constraints = new DecisionConstraints
            {
                Rules = new List<DecisionRule>
                {
                    new DecisionRule { Field = "status", EqualsValue = "hot", Select = "call" }
                }
            };

            var decision = await actor.Context!.Decide(request);

            Assert.AreEqual("call", decision.Value);
            Assert.AreEqual(DecisionFabricIds.Rules, decision.Provider);
            Assert.AreEqual(0, laya.Calls);
        }

        [TestMethod]
        public async Task RuntimeWithoutFabric_DoesNotBindContext()
        {
            var runtime = new InMemoryActorRuntime();
            await runtime.InitializeAsync(new Dictionary<string, object>());
            var actor = await runtime.SpawnActorAsync("sales-4", id => new SalesActor(id));
            Assert.IsNull(actor.Context);
        }

        private static async Task<InMemoryActorRuntime> StartRuntime(ScriptedProvider laya)
        {
            var llm = new ScriptedProvider("openai", DecisionEngineRole.Llm, false, 800, 0.01m, 0.9);
            var providers = new IDecisionProvider[] { new RuleDecisionProvider(), laya, llm };
            var options = new DecisionFabricOptions();
            var service = new DecisionService(new DecisionRouter(providers, options), options, new DecisionTelemetry());
            var runtime = new InMemoryActorRuntime(LoggerFactory.CreateLogger("actor-decide-tests"), service);
            await runtime.InitializeAsync(new Dictionary<string, object>());
            return runtime;
        }

        /// <summary>
        /// Acceptance shape: the actor asks Context or Decide and never a provider.
        /// </summary>
        private sealed class SalesActor : Actor
        {
            public SalesActor(string id)
                : base(id)
            {
            }

            public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
            {
                var decision = await Decide(Choice.Create("What action should I take?", "call", "email", "wait"), cancellationToken);
                return new MessageEnvelope(decision);
            }
        }
    }
}
