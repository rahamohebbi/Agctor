using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Decisions;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Utils.Observability.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Decisions
{
    [TestClass]
    public class DecisionTelemetryTests
    {
        [TestMethod]
        public async Task EmitsEvents_AndOpenTelemetryInstruments()
        {
            var measurements = new List<string>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == DecisionTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => measurements.Add(instrument.Name));
            listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
            listener.Start();

            var events = new List<DecisionEvent>();
            var telemetry = new DecisionTelemetry(new[] { new ListListener(events) });
            var laya = new ScriptedProvider("laya", DecisionEngineRole.Laya, true, 40, 0m, 0.8, _ =>
                new DecisionResult { Value = "email", Confidence = 0.43, Cost = 0m });
            var llm = new ScriptedProvider("openai", DecisionEngineRole.Llm, false, 800, 0.01m, 0.9, _ =>
                new DecisionResult { Value = "call", Confidence = 0.9, Cost = 0.01m });
            var options = new DecisionFabricOptions();
            var service = new DecisionService(
                new DecisionRouter(new IDecisionProvider[] { new AgctorSDK.Core.Decisions.Providers.RuleDecisionProvider(), laya, llm }, options),
                options,
                telemetry);

            await service.DecideAsync(Choice.Create("What action should I take?", "call", "email"));

            CollectionAssert.IsSubsetOf(
                new[]
                {
                    DecisionEventKind.DecisionRequested,
                    DecisionEventKind.ProviderSelected,
                    DecisionEventKind.DecisionEscalated,
                    DecisionEventKind.DecisionCompleted
                },
                events.Select(e => e.Kind).Distinct().ToList());

            CollectionAssert.IsSubsetOf(
                new[]
                {
                    MetricsConstants.Decisions.Latency,
                    MetricsConstants.Decisions.Cost,
                    MetricsConstants.Decisions.Confidence,
                    MetricsConstants.Decisions.Escalations
                },
                measurements.Distinct().ToList());

            telemetry.Dispose();
        }

        [TestMethod]
        public void ConfigurationSection_BindsDecisionFabric()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DecisionFabric:DefaultProvider"] = "laya",
                    ["DecisionFabric:Routing:ConfidenceThreshold"] = "0.70",
                    ["DecisionFabric:Routing:EnableFallback"] = "true",
                    ["DecisionFabric:Routing:EnableEscalation"] = "true",
                    ["DecisionFabric:Providers:Laya:Enabled"] = "true",
                    ["DecisionFabric:Providers:Laya:Endpoint"] = "http://localhost:8080",
                    ["DecisionFabric:Providers:OpenAI:Enabled"] = "true",
                    ["DecisionFabric:Providers:OpenAI:Model"] = "configured-model",
                    ["DecisionFabric:Providers:Rules:Enabled"] = "true"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddDecisionFabric(configuration.GetSection(DecisionFabricOptions.SectionName));
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DecisionFabricOptions>>().Value;

            Assert.AreEqual("laya", options.DefaultProvider);
            Assert.AreEqual(0.70, options.Routing.ConfidenceThreshold, 0.001);
            Assert.IsTrue(options.Routing.EnableEscalation);
            Assert.AreEqual("http://localhost:8080", options.Providers.Laya.Endpoint);
            Assert.AreEqual("configured-model", options.Providers.OpenAI.Model);
            Assert.IsTrue(options.Providers.Rules.Enabled);

            var names = provider.GetServices<IDecisionProvider>().Select(item => item.Name).ToList();
            CollectionAssert.IsSubsetOf(
                new[] { DecisionFabricIds.Rules, DecisionFabricIds.Laya, DecisionFabricIds.OpenAI },
                names);
        }

        private sealed class ListListener : IDecisionEventListener
        {
            private readonly List<DecisionEvent> _events;
            public ListListener(List<DecisionEvent> events) => _events = events;
            public void OnEvent(DecisionEvent decisionEvent) => _events.Add(decisionEvent);
        }
    }
}
