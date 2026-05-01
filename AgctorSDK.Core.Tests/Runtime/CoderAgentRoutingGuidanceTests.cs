using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Runtime
{
    [TestClass]
    public class CoderAgentRoutingGuidanceTests
    {
        [TestMethod]
        public async Task CoderAgent_NaturalLanguagePrompt_ReturnsRoutingGuidance()
        {
            var runtime = new CoderGuidanceStubRuntimeAdapter();
            var services = new ServiceCollection().BuildServiceProvider();
            var factory = new AgentFactory(runtime, services, new AgctorConsoleLogger(), new InMemoryAgentRegistry());
            var agent = new CoderAgent("coder-agent");
            agent.SetAgentFactory(factory);

            var envelope = new MessageEnvelope(
                "add multiplication to MathUtils",
                metadata: new Dictionary<string, object> { ["CorrelationId"] = "corr-guidance" },
                headers: new Dictionary<string, string>
                {
                    ["MessageType"] = "Prompt",
                    ["SenderId"] = "http-api"
                });

            await agent.ReceiveAsync(envelope, CancellationToken.None);
            await runtime.WaitForReplyAsync();

            Assert.IsNotNull(runtime.LastReply);
            Assert.IsFalse(runtime.LastReply!.IsSuccess);
            StringAssert.Contains(runtime.LastReply.Error ?? string.Empty, "CodeEditorTool command");
            StringAssert.Contains(runtime.LastReply.Error ?? string.Empty, "refactor-agent");
        }

        private sealed class CoderGuidanceStubRuntimeAdapter : IActorRuntimeAdapter
        {
            private readonly TaskCompletionSource<bool> _replyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public ToolResult? LastReply { get; private set; }

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
                if (targetActorId == "coder-agent" && message is MessageEnvelope envelope && envelope.Payload is ToolResult tr)
                {
                    LastReply = tr;
                    _replyTcs.TrySetResult(true);
                }
                return Task.CompletedTask;
            }

            public Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) where TResponse : class
            {
                return Task.FromResult((TResponse)(object)string.Empty);
            }

            public async Task WaitForReplyAsync()
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _replyTcs.Task.WaitAsync(cts.Token);
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
