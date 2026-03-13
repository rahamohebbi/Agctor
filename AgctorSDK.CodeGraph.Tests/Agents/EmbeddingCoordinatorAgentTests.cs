using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Agents
{
    [TestClass]
    public class EmbeddingCoordinatorAgentTests
    {
        [TestMethod]
        public async Task EnsureEmbeddingsReady_ShouldRunIndexerOnlyOnce_ForConcurrentRequests()
        {
            var runtime = new InMemoryActorRuntime();
            await runtime.InitializeAsync(new Dictionary<string, object>());

            var registry = new InMemoryAgentRegistry();
            var services = new ServiceCollection().BuildServiceProvider();
            var factory = new AgentFactory(runtime, services, new AgctorConsoleLogger(), registry);

            var indexer = new TestIndexerAgent("indexer-agent", delayMs: 50);
            var coordinator = new EmbeddingCoordinatorAgent("embedding-coordinator-agent", "indexer-agent");
            coordinator.SetAgentFactory(factory);

            await RegisterAsync(runtime, registry, indexer);
            await RegisterAsync(runtime, registry, coordinator);

            var first = runtime.SendMessageAsync<EmbeddingReadyResult>(
                "embedding-coordinator-agent",
                new EnsureEmbeddingsReadyMessage(),
                TimeSpan.FromSeconds(5),
                senderId: "test");
            var second = runtime.SendMessageAsync<EmbeddingReadyResult>(
                "embedding-coordinator-agent",
                new EnsureEmbeddingsReadyMessage(),
                TimeSpan.FromSeconds(5),
                senderId: "test");

            var results = await Task.WhenAll(first, second);

            Assert.AreEqual(1, indexer.IndexRuns);
            Assert.IsTrue(results[0].IsReady);
            Assert.IsTrue(results[1].IsReady);
            Assert.AreEqual(EmbeddingLifecycleState.Ready, results[1].State);
            Assert.AreEqual(1, results[1].GraphVersion);
            Assert.AreEqual(1, results[1].IndexedGraphVersion);
        }

        [TestMethod]
        public async Task MarkEmbeddingsStale_ShouldRequireReindexBeforeReadyAgain()
        {
            var runtime = new InMemoryActorRuntime();
            await runtime.InitializeAsync(new Dictionary<string, object>());

            var registry = new InMemoryAgentRegistry();
            var services = new ServiceCollection().BuildServiceProvider();
            var factory = new AgentFactory(runtime, services, new AgctorConsoleLogger(), registry);

            var indexer = new TestIndexerAgent("indexer-agent");
            var coordinator = new EmbeddingCoordinatorAgent("embedding-coordinator-agent", "indexer-agent");
            coordinator.SetAgentFactory(factory);

            await RegisterAsync(runtime, registry, indexer);
            await RegisterAsync(runtime, registry, coordinator);

            var initial = await runtime.SendMessageAsync<EmbeddingReadyResult>(
                "embedding-coordinator-agent",
                new EnsureEmbeddingsReadyMessage(),
                TimeSpan.FromSeconds(5),
                senderId: "test");
            var stale = await runtime.SendMessageAsync<EmbeddingStatusResult>(
                "embedding-coordinator-agent",
                new MarkEmbeddingsStaleMessage("edit"),
                TimeSpan.FromSeconds(5),
                senderId: "test");
            var refreshed = await runtime.SendMessageAsync<EmbeddingReadyResult>(
                "embedding-coordinator-agent",
                new EnsureEmbeddingsReadyMessage(),
                TimeSpan.FromSeconds(5),
                senderId: "test");

            Assert.IsTrue(initial.IsReady);
            Assert.AreEqual(EmbeddingLifecycleState.Stale, stale.State);
            Assert.AreEqual(2, stale.GraphVersion);
            Assert.AreEqual(1, stale.IndexedGraphVersion);
            Assert.IsTrue(refreshed.IsReady);
            Assert.AreEqual(2, refreshed.GraphVersion);
            Assert.AreEqual(2, refreshed.IndexedGraphVersion);
            Assert.AreEqual(2, indexer.IndexRuns);
        }

        private static async Task RegisterAsync(InMemoryActorRuntime runtime, InMemoryAgentRegistry registry, Agent agent)
        {
            await agent.InitializeAsync();
            await runtime.RegisterActorAsync(agent);
            await registry.RegisterAgentAsync(agent);
        }

        private sealed class TestIndexerAgent : Agent
        {
            private readonly int _delayMs;

            public TestIndexerAgent(string id, int delayMs = 0) : base(id)
            {
                _delayMs = delayMs;
            }

            public int IndexRuns { get; private set; }

            protected override async Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken)
            {
                IndexRuns++;
                if (_delayMs > 0)
                {
                    await Task.Delay(_delayMs, cancellationToken);
                }

                await FinalizeTask("IndexingComplete", cancellationToken);
            }

            protected override bool ShouldDecomposeTask(string prompt) => false;
        }
    }
}
