using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Decisions;
using AgctorSDK.Core.Decisions.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Decisions
{
    [TestClass]
    public class DecisionProviderTests
    {
        [TestMethod]
        public void ChoiceCreate_BuildsOptions()
        {
            var request = Choice.Create("What action should I take?", "call", "email", "wait");
            Assert.AreEqual(DecisionType.Choice, request.Type);
            Assert.AreEqual(3, request.Options!.Count);
            Assert.AreEqual("call", request.Options[0].Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(request.Id));
        }

        [TestMethod]
        public async Task Rules_DecideWeightedChoice_ScoreAndBinary()
        {
            var rules = new RuleDecisionProvider();

            var weighted = Choice.Create("next?", "call", "email", "wait");
            weighted.Options![0].Metadata = new Dictionary<string, object> { ["weight"] = 1 };
            weighted.Options[1].Metadata = new Dictionary<string, object> { ["weight"] = 4 };
            weighted.Options[2].Metadata = new Dictionary<string, object> { ["weight"] = 1 };

            var score = Score.Create("fit?", 0, 100);
            score.Constraints!.Rules = new List<DecisionRule> { new DecisionRule { Score = 87, Confidence = 1 } };

            var binary = Binary.Create("contact now?");
            binary.State = new Prospect { Stage = "ready" };
            binary.Constraints = new DecisionConstraints
            {
                Rules = new List<DecisionRule>
                {
                    new DecisionRule { Field = "Stage", EqualsValue = "ready", Value = true, Confidence = 1 }
                }
            };

            Assert.IsTrue(rules.CanHandle(weighted));
            Assert.IsFalse(rules.CanHandle(Choice.Create("open question?", "call", "email")));

            var choiceResult = await rules.DecideAsync(weighted);
            var scoreResult = await rules.DecideAsync(score);
            var binaryResult = await rules.DecideAsync(binary);

            Assert.AreEqual("email", choiceResult.Value);
            Assert.AreEqual(1d, choiceResult.Confidence, 0.001);
            Assert.AreEqual(87d, scoreResult.Value);
            Assert.AreEqual(true, binaryResult.Value);
        }

        [TestMethod]
        public async Task Laya_TranslatesRequest_AndReadsResponse()
        {
            string? captured = null;
            var handler = new StubHandler(request =>
            {
                captured = request;
                return Json(HttpStatusCode.OK, """
                    {"selected":"call","confidence":0.82,"scores":{"call":0.82,"email":0.11,"wait":0.07},"summary":"Call first","cost":0}
                    """);
            });
            var provider = new LayaDecisionProvider(new HttpClient(handler), new LayaProviderOptions
            {
                Endpoint = "http://localhost:8080",
                DecidePath = "/v1/decisions"
            });

            var result = await provider.DecideAsync(Choice.Create("What should the sales agent do next?", "call", "email", "wait"));

            Assert.IsNotNull(captured);
            Assert.IsTrue(captured!.Contains("\"type\":\"choice\"", StringComparison.Ordinal));
            Assert.IsTrue(captured.Contains("localhost:8080/v1/decisions", StringComparison.Ordinal) || handler.LastUri!.AbsolutePath == "/v1/decisions");
            Assert.AreEqual("call", result.Value);
            Assert.AreEqual(0.82, result.Confidence, 0.001);
            Assert.AreEqual(0.82, result.Scores!["call"], 0.001);
            Assert.AreEqual("Call first", result.ReasoningSummary);
            Assert.AreEqual(DecisionFabricIds.Laya, result.Provider);
        }

        [TestMethod]
        public async Task OpenAi_TranslatesToChatCompletions_WithoutLiveKey()
        {
            string? captured = null;
            var handler = new StubHandler(request =>
            {
                captured = request;
                var content = JsonSerializer.Serialize(new
                {
                    value = "call",
                    confidence = 0.91,
                    scores = new { call = 0.91, email = 0.09 },
                    summary = "Prefer a call"
                });
                var envelope = JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { role = "assistant", content } } }
                });
                return Json(HttpStatusCode.OK, envelope);
            });

            var provider = new OpenAiDecisionProvider(new HttpClient(handler), new OpenAiProviderOptions
            {
                Endpoint = "https://example.test",
                Model = "configured-model",
                Name = "openai"
            });

            var request = Choice.Create("What action should I take?", "call", "email");
            var result = await provider.DecideAsync(request);

            Assert.IsNotNull(captured);
            using var document = JsonDocument.Parse(BodyOf(captured!));
            Assert.AreEqual("configured-model", document.RootElement.GetProperty("model").GetString());
            var system = document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
            StringAssert.Contains(system, "no chain of thought");
            Assert.AreEqual("call", result.Value?.ToString());
            Assert.AreEqual(0.91, result.Confidence, 0.001);
            Assert.AreEqual("Prefer a call", result.ReasoningSummary);
        }

        [TestMethod]
        public async Task LayaHttpFailure_ThrowsDecisionException()
        {
            var handler = new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));
            var provider = new LayaDecisionProvider(new HttpClient(handler), new LayaProviderOptions { Endpoint = "http://localhost:8080" });
            await Assert.ThrowsExceptionAsync<DecisionException>(() =>
                provider.DecideAsync(Binary.Create("now?")));
        }

        private sealed class Prospect
        {
            public string? Stage { get; set; }
        }

        private static string BodyOf(string raw)
        {
            var split = raw.IndexOf('\n');
            return split < 0 ? raw : raw.Substring(split + 1);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<string, HttpResponseMessage> _respond;
            public Uri? LastUri { get; private set; }

            public StubHandler(Func<string, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastUri = request.RequestUri;
                var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                return _respond((request.RequestUri?.ToString() ?? string.Empty) + "\n" + body);
            }
        }
    }
}
