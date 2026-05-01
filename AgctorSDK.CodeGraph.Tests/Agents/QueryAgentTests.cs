using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Agents
{
    [TestClass]
    public class QueryAgentTests
    {
        [TestMethod]
        public async Task QueryAgent_ShouldRouteCodeChangePromptsToCoderWhenSearchReturnsNoContext()
        {
            var runtime = new StubRuntimeAdapter(searchResponse: string.Empty);
            var agent = CreateAgent(runtime);

            var response = await agent.ReceiveAsync(new MessageEnvelope(
                "create a new file",
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" }));

            Assert.IsInstanceOfType<string>(response.Payload);
            var answer = (string)response.Payload;
            StringAssert.Contains(answer, "cannot create, edit, or delete files");
            StringAssert.Contains(answer, "coder-agent");
        }

        [TestMethod]
        public async Task QueryAgent_ShouldSuggestIndexingForReadQueriesWhenSearchReturnsNoContext()
        {
            var runtime = new StubRuntimeAdapter(searchResponse: string.Empty);
            var agent = CreateAgent(runtime);

            var response = await agent.ReceiveAsync(new MessageEnvelope(
                "Where is Square defined?",
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" }));

            Assert.IsInstanceOfType<string>(response.Payload);
            var answer = (string)response.Payload;
            StringAssert.Contains(answer, "Click Index now");
            StringAssert.Contains(answer, "existing code");
        }

        private static QueryAgent CreateAgent(StubRuntimeAdapter runtime)
        {
            var agent = new QueryAgent("query-agent", "search-agent", "llm-agent");
            var services = new ServiceCollection().BuildServiceProvider();
            var factory = new AgentFactory(runtime, services, new AgctorConsoleLogger(), new InMemoryAgentRegistry());
            agent.SetAgentFactory(factory);
            return agent;
        }

        private sealed class StubRuntimeAdapter : IActorRuntimeAdapter
        {
            private readonly string _searchResponse;

            public StubRuntimeAdapter(string searchResponse)
            {
                _searchResponse = searchResponse;
            }

            public string Name => "StubRuntime";
            public string Version => "1.0.0";
            public bool IsInitialized => true;
            public IReadOnlyDictionary<string, object> Configuration => new Dictionary<string, object>();

            public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned
            {
                add { }
                remove { }
            }

            public event EventHandler<ActorStoppedEventArgs>? ActorStopped
            {
                add { }
                remove { }
            }

            public event EventHandler<MessageSentEventArgs>? MessageSent
            {
                add { }
                remove { }
            }

            public event EventHandler<DeadLetterEventArgs>? DeadLetter
            {
                add { }
                remove { }
            }

            public Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RegisterActorAsync(IActor actor, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor => Task.FromResult<T?>(null);
            public Task SendMessageAsync(string targetActorId, object message, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task StopActorAsync(string actorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
            public Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IRuntimeStatistics>(new StubRuntimeStatistics());
            public Task<string> RequestHumanInputAsync(string requestingAgentId, string prompt, string instructions, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

            public Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
            {
                throw new NotSupportedException();
            }

            public Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> actorFactory, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
            {
                throw new NotSupportedException();
            }

            public Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) where TResponse : class
            {
                if (targetActorId == "llm-agent")
                {
                    Assert.Fail("LLM should not be called when search returns no context.");
                }

                return Task.FromResult((TResponse)(object)_searchResponse);
            }

            public void Dispose()
            {
            }
        }

        private sealed class StubRuntimeStatistics : IRuntimeStatistics
        {
            public int ActiveActorCount => 0;
            public long TotalMessagesProcessed => 0;
            public double MessagesPerSecond => 0;
            public double AverageMessageProcessingTime => 0;
            public TimeSpan Uptime => TimeSpan.Zero;
            public long MemoryUsageBytes => 0;
            public IReadOnlyDictionary<string, object> AdditionalMetrics => new Dictionary<string, object>();
        }
    }
}
