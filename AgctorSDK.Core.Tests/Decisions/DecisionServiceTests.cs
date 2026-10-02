using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Decisions;
using AgctorSDK.Core.Decisions.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Decisions
{
    [TestClass]
    public class DecisionServiceTests
    {
        [TestMethod]
        public async Task LowConfidence_EscalatesToLlm_AndRecordsPreviousProvider()
        {
            var laya = Provider("laya", DecisionEngineRole.Laya, _ => Result("email", 0.43));
            var llm = Provider("openai", DecisionEngineRole.Llm, _ => Result("call", 0.91, "Call while the lead is warm"));
            var events = new List<DecisionEvent>();
            var service = Build(laya, llm, events);

            var result = await service.DecideAsync(Choice.Create("What action should I take?", "call", "email", "wait"));

            Assert.AreEqual("call", result.Value);
            Assert.AreEqual(DecisionFabricIds.OpenAI, result.Provider);
            Assert.AreEqual(DecisionFabricIds.Laya, result.PreviousProvider);
            Assert.IsTrue(result.Escalated);
            Assert.AreEqual(0.91, result.Confidence, 0.001);
            Assert.IsTrue(result.Latency >= TimeSpan.Zero);
            Assert.IsTrue(result.LatencyMs >= 0);
            Assert.IsTrue(events.Any(e => e.Kind == DecisionEventKind.DecisionEscalated && e.PreviousProvider == "laya"));
            Assert.IsTrue(events.Any(e => e.Kind == DecisionEventKind.DecisionCompleted && e.Escalated == true));
            Assert.AreEqual(1, laya.Calls);
            Assert.AreEqual(1, llm.Calls);
        }

        [TestMethod]
        public async Task ConfidentLayaResult_DoesNotEscalate()
        {
            var laya = Provider("laya", DecisionEngineRole.Laya, _ => Result("call", 0.86));
            var llm = Provider("openai", DecisionEngineRole.Llm, _ => Result("email", 0.99));
            var service = Build(laya, llm, new List<DecisionEvent>());

            var result = await service.DecideAsync(Choice.Create("What action should I take?", "call", "email", "wait"));

            Assert.AreEqual("call", result.Value);
            Assert.AreEqual("laya", result.Provider);
            Assert.IsFalse(result.Escalated);
            Assert.IsNull(result.PreviousProvider);
            Assert.AreEqual(0, llm.Calls);
        }

        [TestMethod]
        public async Task PolicyMinimumConfidence_RaisesTheBar()
        {
            var laya = Provider("laya", DecisionEngineRole.Laya, _ => Result("wait", 0.75));
            var llm = Provider("openai", DecisionEngineRole.Llm, _ => Result("call", 0.88));
            var service = Build(laya, llm, new List<DecisionEvent>());
            var request = Choice.Create("What action should I take?", "call", "email", "wait");
            request.Policy = new DecisionPolicy { MinimumConfidence = 0.80 };

            var result = await service.DecideAsync(request);

            Assert.IsTrue(result.Escalated);
            Assert.AreEqual("call", result.Value);
            Assert.AreEqual("laya", result.PreviousProvider);
        }

        [TestMethod]
        public async Task ProviderFailure_FallsBackToNextProvider()
        {
            var laya = Provider("laya", DecisionEngineRole.Laya, _ => Result("email", 0.9));
            laya.Error = new InvalidOperationException("laya down");
            var llm = Provider("openai", DecisionEngineRole.Llm, _ => Result("call", 0.93));
            var events = new List<DecisionEvent>();
            var service = Build(laya, llm, events);

            var result = await service.DecideAsync(Choice.Create("What action should I take?", "call", "email"));

            Assert.AreEqual("call", result.Value);
            Assert.AreEqual("openai", result.Provider);
            Assert.IsFalse(result.Escalated);
            Assert.IsTrue(events.Any(e => e.Kind == DecisionEventKind.DecisionFailed && e.Provider == "laya"));
            Assert.IsTrue(events.Any(e => e.Kind == DecisionEventKind.DecisionCompleted));
        }

        [TestMethod]
        public async Task AllProvidersFail_ThrowsAndEmitsFailure()
        {
            var laya = Provider("laya", DecisionEngineRole.Laya, _ => Result("email", 0.9));
            laya.Error = new InvalidOperationException("laya down");
            var llm = Provider("openai", DecisionEngineRole.Llm, _ => Result("call", 0.9));
            llm.Error = new InvalidOperationException("llm down");
            var events = new List<DecisionEvent>();
            var service = Build(laya, llm, events);

            await Assert.ThrowsExceptionAsync<DecisionException>(() =>
                service.DecideAsync(Choice.Create("What action should I take?", "call", "email")));

            Assert.IsTrue(events.Any(e => e.Kind == DecisionEventKind.DecisionFailed));
            Assert.IsFalse(events.Any(e => e.Kind == DecisionEventKind.DecisionCompleted));
        }

        [TestMethod]
        public async Task ChoiceScoreAndBinary_AreNormalized()
        {
            var service = new DecisionService(
                new DecisionRouter(new[] { new RuleDecisionProvider() }, new DecisionFabricOptions()),
                new DecisionFabricOptions());

            var choice = Choice.Create("next?", "call", "email");
            choice.State = new { status = "hot" };
            choice.Constraints = new DecisionConstraints
            {
                Rules = new List<DecisionRule> { new DecisionRule { Field = "status", EqualsValue = "hot", Select = "call" } }
            };

            var score = Score.Create("fit?", 0, 100);
            score.Constraints!.Rules = new List<DecisionRule> { new DecisionRule { Score = 150, Confidence = 0.76 } };

            var binary = Binary.Create("now?");
            binary.Constraints = new DecisionConstraints
            {
                Rules = new List<DecisionRule> { new DecisionRule { Value = true, Confidence = 0.91 } }
            };

            var choiceResult = await service.DecideAsync(choice);
            var scoreResult = await service.DecideAsync(score);
            var binaryResult = await service.DecideAsync(binary);

            Assert.AreEqual("call", choiceResult.Value);
            Assert.AreEqual(DecisionFabricIds.Rules, choiceResult.Provider);
            Assert.AreEqual(100d, scoreResult.Value);
            Assert.AreEqual(0.76, scoreResult.Confidence, 0.001);
            Assert.AreEqual(true, binaryResult.Value);
            Assert.AreEqual(0.91, binaryResult.Confidence, 0.001);
        }

        private static ScriptedProvider Provider(string name, DecisionEngineRole role, Func<DecisionRequest, DecisionResult> decide)
        {
            var local = role != DecisionEngineRole.Llm;
            var latency = role == DecisionEngineRole.Llm ? 800 : 40;
            var cost = role == DecisionEngineRole.Llm ? 0.01m : 0m;
            var accuracy = role == DecisionEngineRole.Llm ? 0.9 : 0.8;
            return new ScriptedProvider(name, role, local, latency, cost, accuracy, decide);
        }

        private static DecisionService Build(ScriptedProvider laya, ScriptedProvider llm, List<DecisionEvent> events)
        {
            var providers = new IDecisionProvider[] { new RuleDecisionProvider(), laya, llm };
            var options = new DecisionFabricOptions();
            var telemetry = new DecisionTelemetry(new[] { new ListListener(events) });
            return new DecisionService(new DecisionRouter(providers, options), options, telemetry);
        }

        private static DecisionResult Result(object value, double confidence, string? summary = null)
        {
            return new DecisionResult
            {
                Value = value,
                Confidence = confidence,
                ReasoningSummary = summary,
                Cost = 0m
            };
        }

        private sealed class ListListener : IDecisionEventListener
        {
            private readonly List<DecisionEvent> _events;
            public ListListener(List<DecisionEvent> events) => _events = events;
            public void OnEvent(DecisionEvent decisionEvent) => _events.Add(decisionEvent);
        }
    }
}
