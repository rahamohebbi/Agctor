using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Sessions.Messages;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Agents
{
    [TestClass]
    public class QueryAgentSessionFollowupTests
    {
        [TestMethod]
        public async Task QueryAgent_FollowupMethodCount_UsesLlmFirst_WhenAvailable()
        {
            var runtime = new SessionAwareStubRuntimeAdapter(llmAvailable: true);
            var agent = CreateAgent(runtime);

            var envelope = new MessageEnvelope(
                "how many methods does MathUtils have?",
                metadata: new Dictionary<string, object> { ["sessionId"] = "session-a" },
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" });

            var response = await agent.ReceiveAsync(envelope, CancellationToken.None);

            Assert.IsInstanceOfType<string>(response.Payload);
            var answer = (string)response.Payload;
            StringAssert.Contains(answer, "MathUtils");
            StringAssert.Contains(answer, "2 method(s)");
            StringAssert.Contains(answer, "Square");
            StringAssert.Contains(answer, "Cube");

            Assert.AreEqual(1, runtime.SearchPrompts.Count);
            Assert.AreEqual("how many methods does MathUtils have?", runtime.SearchPrompts[0]);
            Assert.AreEqual(1, runtime.LlmCallCount);
        }

        [TestMethod]
        public async Task QueryAgent_FollowupMethodCount_UsesDeterministicBackup_WhenLlmUnavailable()
        {
            var runtime = new SessionAwareStubRuntimeAdapter(llmAvailable: false);
            var agent = CreateAgent(runtime);

            var envelope = new MessageEnvelope(
                "how many methods does MathUtils have?",
                metadata: new Dictionary<string, object> { ["sessionId"] = "session-a" },
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" });

            var response = await agent.ReceiveAsync(envelope, CancellationToken.None);

            Assert.IsInstanceOfType<string>(response.Payload);
            var answer = (string)response.Payload;
            StringAssert.Contains(answer, "MathUtils");
            StringAssert.Contains(answer, "2 method(s)");
            StringAssert.Contains(answer, "Square");
            StringAssert.Contains(answer, "Cube");

            Assert.AreEqual(2, runtime.SearchPrompts.Count);
            Assert.AreEqual("how many methods does MathUtils have?", runtime.SearchPrompts[0]);
            Assert.AreEqual("list methods in class MathUtils", runtime.SearchPrompts[1]);
            Assert.AreEqual(1, runtime.LlmCallCount);
        }

        private static QueryAgent CreateAgent(SessionAwareStubRuntimeAdapter runtime)
        {
            var agent = new QueryAgent("query-agent", "search-agent", "llm-agent");
            var services = new ServiceCollection().BuildServiceProvider();
            var factory = new AgentFactory(runtime, services, new AgctorConsoleLogger(), new InMemoryAgentRegistry());
            agent.SetAgentFactory(factory);
            return agent;
        }

        private sealed class SessionAwareStubRuntimeAdapter : IActorRuntimeAdapter
        {
            private readonly bool _llmAvailable;
            public List<string> SearchPrompts { get; } = new();
            public int LlmCallCount { get; private set; }

            public SessionAwareStubRuntimeAdapter(bool llmAvailable)
            {
                _llmAvailable = llmAvailable;
            }

            public string Name => "StubRuntime";
            public string Version => "1.0.0";
            public bool IsInitialized => true;
            public IReadOnlyDictionary<string, object> Configuration => new Dictionary<string, object>();

            public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned { add { } remove { } }
            public event EventHandler<ActorStoppedEventArgs>? ActorStopped { add { } remove { } }
            public event EventHandler<MessageSentEventArgs>? MessageSent { add { } remove { } }
            public event EventHandler<DeadLetterEventArgs>? DeadLetter { add { } remove { } }

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
                if (targetActorId == "session-coordinator-agent")
                {
                    var pkg = new SessionContextPackage
                    {
                        SessionId = "session-a",
                        CurrentPrompt = "how many methods does MathUtils have?",
                        Summary = "Prior discussion was about MathUtils methods.",
                        RecentTurns = new List<SessionTurn>
                        {
                            new() { SessionId = "session-a", Sequence = 1, Role = SessionRole.User, Content = "what does MathUtils do ?" },
                            new() { SessionId = "session-a", Sequence = 2, Role = SessionRole.Assistant, Content = "It has methods Square and Cube." }
                        },
                        PromptContext = "user: what does MathUtils do ?\nassistant: It has methods Square and Cube."
                    };
                    return Task.FromResult((TResponse)(object)pkg);
                }

                if (targetActorId == "search-agent")
                {
                    var prompt = message as string ?? string.Empty;
                    SearchPrompts.Add(prompt);

                    if (prompt.Equals("how many methods does MathUtils have?", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult((TResponse)(object)"Square\nCube");
                    }

                    if (prompt.Equals("list methods in class MathUtils", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult((TResponse)(object)"Square\nCube");
                    }

                    return Task.FromResult((TResponse)(object)string.Empty);
                }

                if (targetActorId == "llm-agent")
                {
                    LlmCallCount++;
                    if (_llmAvailable)
                    {
                        return Task.FromResult((TResponse)(object)"`MathUtils` has 2 method(s): Square, Cube.");
                    }

                    return Task.FromResult((TResponse)(object)"Error: LLM unavailable.");
                }

                return Task.FromResult((TResponse)(object)string.Empty);
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
