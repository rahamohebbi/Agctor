using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.Decisions;
using AgctorSDK.Core.Decisions.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Decisions
{
    [TestClass]
    public class DecisionRouterTests
    {
        private readonly RuleDecisionProvider _rules = new RuleDecisionProvider();
        private readonly ScriptedProvider _laya;
        private readonly ScriptedProvider _llm;
        private readonly DecisionRouter _router;

        public DecisionRouterTests()
        {
            _laya = new ScriptedProvider("laya", DecisionEngineRole.Laya, local: true, latencyMs: 40, cost: 0m, accuracy: 0.8);
            _llm = new ScriptedProvider("openai", DecisionEngineRole.Llm, local: false, latencyMs: 800, cost: 0.01m, accuracy: 0.9);
            _router = new DecisionRouter(new IDecisionProvider[] { _rules, _laya, _llm }, new DecisionFabricOptions());
        }

        [TestMethod]
        public async Task DeterministicRequest_SelectsRules()
        {
            var request = HotLead();
            var selected = await _router.SelectProviderAsync(request, new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.Rules, selected.Name);
        }

        [TestMethod]
        public async Task SimpleChoice_SelectsLaya()
        {
            var selected = await _router.SelectProviderAsync(
                Choice.Create("What action should I take?", "call", "email", "wait"),
                new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.Laya, selected.Name);
        }

        [TestMethod]
        public async Task SimpleScoreAndBinary_SelectLaya()
        {
            var score = await _router.SelectProviderAsync(Score.Create("How qualified is this prospect?"), new DecisionContext());
            var binary = await _router.SelectProviderAsync(Binary.Create("Should the agent contact this customer now?"), new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.Laya, score.Name);
            Assert.AreEqual(DecisionFabricIds.Laya, binary.Name);
        }

        [TestMethod]
        public async Task ComplexRequest_SelectsLlm()
        {
            var request = Choice.Create("What action should I take?", "call", "email", "wait");
            request.Metadata = new Dictionary<string, object> { ["complexity"] = "complex" };
            var selected = await _router.SelectProviderAsync(request, new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.OpenAI, selected.Name);
        }

        [TestMethod]
        public async Task PreferAccurate_UpgradesSimpleChoiceToLlm()
        {
            var request = Choice.Create("What action should I take?", "call", "email", "wait");
            request.Policy = new DecisionPolicy { PreferAccurate = true };
            var selected = await _router.SelectProviderAsync(request, new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.OpenAI, selected.Name);
        }

        [TestMethod]
        public async Task RequireLocal_BlocksRemoteLlm()
        {
            var request = Choice.Create("What action should I take?", "call", "email");
            request.Metadata = new Dictionary<string, object> { ["requiresReasoning"] = true };
            request.Policy = new DecisionPolicy { RequireLocal = true };
            var selected = await _router.SelectProviderAsync(request, new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.Laya, selected.Name);
        }

        [TestMethod]
        public async Task BlockedAndAllowedProviders_AreHonored()
        {
            var request = Choice.Create("What action should I take?", "call", "email");
            request.Policy = new DecisionPolicy { BlockedProviders = new List<string> { "laya" } };
            var blocked = await _router.SelectProviderAsync(request, new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.OpenAI, blocked.Name);

            request.Policy = new DecisionPolicy { AllowedProviders = new List<string> { "rules" } };
            await Assert.ThrowsExceptionAsync<DecisionException>(() => _router.SelectProviderAsync(request, new DecisionContext()));
        }

        [TestMethod]
        public async Task MaxCostAndMaxLatency_ExcludeLlm()
        {
            var request = Choice.Create(new string('q', 300), "call", "email");
            request.Policy = new DecisionPolicy { MaxCost = 0m };
            var byCost = await _router.SelectProviderAsync(request, new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.Laya, byCost.Name);

            request.Policy = new DecisionPolicy { MaxLatencyMs = 500 };
            var byLatency = await _router.SelectProviderAsync(request, new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.Laya, byLatency.Name);
        }

        [TestMethod]
        public async Task UnavailableLaya_FallsThroughToLlm()
        {
            _laya.IsAvailable = false;
            var selected = await _router.SelectProviderAsync(
                Choice.Create("What action should I take?", "call", "email"),
                new DecisionContext());
            Assert.AreEqual(DecisionFabricIds.OpenAI, selected.Name);
        }

        private static DecisionRequest HotLead()
        {
            var request = Choice.Create("What should the sales agent do next?", "call", "email", "wait", "drop");
            request.State = new Dictionary<string, object> { ["status"] = "hot" };
            request.Constraints = new DecisionConstraints
            {
                Rules = new List<DecisionRule>
                {
                    new DecisionRule { Field = "status", EqualsValue = "hot", Select = "call", Confidence = 1 }
                }
            };
            return request;
        }
    }
}
