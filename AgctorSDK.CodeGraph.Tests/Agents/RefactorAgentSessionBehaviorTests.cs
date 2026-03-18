using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Agents
{
    [TestClass]
    public class RefactorAgentSessionBehaviorTests
    {
        [TestMethod]
        public async Task RefactorAgent_SearchFallback_UsesSessionContext_WhenPrimaryContextIsEmpty()
        {
            var runtime = new RefactorStubRuntimeAdapter(repairMode: false);
            var agent = CreateAgent(runtime);

            var envelope = new MessageEnvelope(
                "add multiplication to MathUtils",
                metadata: new Dictionary<string, object> { ["sessionId"] = "session-a", ["CorrelationId"] = "corr-1" },
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt", ["SenderId"] = "http-api" });

            await agent.ReceiveAsync(envelope, CancellationToken.None);
            await runtime.WaitForFinalResultAsync();

            Assert.AreEqual(2, runtime.SearchPrompts.Count, "Expected primary search + session-augmented fallback search.");
            Assert.AreEqual("add multiplication to MathUtils", runtime.SearchPrompts[0]);
            StringAssert.Contains(runtime.SearchPrompts[1], "RECENT_SESSION_CONTEXT:");
            StringAssert.Contains(runtime.LastResultPayload ?? string.Empty, "File MathUtils.cs updated");
            Assert.AreEqual("session-a", runtime.LastCoderSessionHeader);
        }

        [TestMethod]
        public async Task RefactorAgent_MalformedLlmResponse_UsesRepairPass()
        {
            var runtime = new RefactorStubRuntimeAdapter(repairMode: true);
            var agent = CreateAgent(runtime);

            var envelope = new MessageEnvelope(
                "add multiplication to MathUtils",
                metadata: new Dictionary<string, object> { ["sessionId"] = "session-a", ["CorrelationId"] = "corr-2" },
                headers: new Dictionary<string, string> { ["MessageType"] = "Prompt", ["SenderId"] = "http-api" });

            await agent.ReceiveAsync(envelope, CancellationToken.None);
            await runtime.WaitForFinalResultAsync();

            Assert.IsTrue(runtime.LlmCallCount >= 2, "Expected a second LLM call for JSON repair.");
            StringAssert.Contains(runtime.LastResultPayload ?? string.Empty, "File MathUtils.cs updated");
        }

        private static RefactorAgent CreateAgent(RefactorStubRuntimeAdapter runtime)
        {
            var agent = new RefactorAgent("refactor-agent", "search-agent", "llm-agent", "coder-agent");
            var services = new ServiceCollection().BuildServiceProvider();
            var factory = new AgentFactory(runtime, services, new AgctorConsoleLogger(), new InMemoryAgentRegistry());
            agent.SetAgentFactory(factory);
            return agent;
        }

        private sealed class RefactorStubRuntimeAdapter : IActorRuntimeAdapter
        {
            private readonly bool _repairMode;
            private readonly TaskCompletionSource<bool> _resultTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public List<string> SearchPrompts { get; } = new();
            public int LlmCallCount { get; private set; }
            public string? LastResultPayload { get; private set; }
            public string? LastCoderSessionHeader { get; private set; }

            public RefactorStubRuntimeAdapter(bool repairMode)
            {
                _repairMode = repairMode;
            }

            public string Name => "StubRuntime";
            public string Version => "1.0.0";
            public bool IsInitialized => true;
            public IReadOnlyDictionary<string, object> Configuration => new Dictionary<string, object>();

            public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned { add { } remove { } }
            public event EventHandler<ActorStoppedEventArgs>? ActorStopped { add { } remove { } }
            public event EventHandler<MessageSentEventArgs>? MessageSent { add { } remove { } }

            public Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RegisterActorAsync(IActor actor, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor => Task.FromResult<T?>(null);
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

            public Task SendMessageAsync(string targetActorId, object message, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
            {
                if (targetActorId == "refactor-agent" && message is MessageEnvelope envelope)
                {
                    LastResultPayload = envelope.Payload?.ToString();
                    _resultTcs.TrySetResult(true);
                }

                return Task.CompletedTask;
            }

            public Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) where TResponse : class
            {
                if (targetActorId == "session-coordinator-agent")
                {
                    var pkg = new SessionContextPackage
                    {
                        SessionId = "session-a",
                        CurrentPrompt = "add multiplication to MathUtils",
                        PromptContext = "user: add it to MathUtils\nassistant: ambiguous target before this turn"
                    };
                    return Task.FromResult((TResponse)(object)pkg);
                }

                if (targetActorId == "search-agent")
                {
                    var prompt = message as string ?? string.Empty;
                    SearchPrompts.Add(prompt);
                    if (prompt.StartsWith("CURRENT_QUESTION:", StringComparison.Ordinal))
                    {
                        return Task.FromResult((TResponse)(object)"Class MathUtils is in MathUtils.cs");
                    }
                    return Task.FromResult((TResponse)(object)string.Empty);
                }

                if (targetActorId == "llm-agent")
                {
                    LlmCallCount++;
                    if (_repairMode && LlmCallCount == 1)
                    {
                        return Task.FromResult((TResponse)(object)"not-json");
                    }

                    if (_repairMode && LlmCallCount == 2)
                    {
                        return Task.FromResult((TResponse)(object)"{\"operation\":\"InsertIntoFile\",\"path\":\"MathUtils.cs\",\"selector\":\"class:MathUtils\",\"content\":\"public static int Multiply(int a, int b) { return a * b; }\"}");
                    }

                    return Task.FromResult((TResponse)(object)"{\"operation\":\"InsertIntoFile\",\"path\":\"MathUtils.cs\",\"selector\":\"class:MathUtils\",\"content\":\"public static int Multiply(int a, int b) { return a * b; }\"}");
                }

                if (targetActorId == "coder-agent")
                {
                    if (headers != null && headers.TryGetValue("session-id", out var sessionValue))
                    {
                        LastCoderSessionHeader = sessionValue;
                    }

                    var result = new ToolResult
                    {
                        IsSuccess = true,
                        Output = "File written to MathUtils.cs"
                    };
                    return Task.FromResult((TResponse)(object)result);
                }

                return Task.FromResult((TResponse)(object)string.Empty);
            }

            public async Task WaitForFinalResultAsync()
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _resultTcs.Task.WaitAsync(cts.Token);
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
