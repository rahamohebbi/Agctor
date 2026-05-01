using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Runtime;

/// <summary>
/// PRD-020 runtime contract checks. These tests define the first supported
/// baseline for actor-runtime behavior and can be reused by other adapters.
/// </summary>
public sealed class InMemoryRuntimeConformanceTests : IDisposable
{
    private readonly InMemoryActorRuntime _runtime = new();

    public void Dispose()
    {
        _runtime.Dispose();
    }

    [Fact]
    public async Task Runtime_Spawns_And_Resolves_Actor()
    {
        await _runtime.InitializeAsync(new Dictionary<string, object>());

        var actor = await _runtime.SpawnActorAsync<ConformanceActor>("conf-actor");
        var resolved = await _runtime.GetActorAsync<ConformanceActor>("conf-actor");

        resolved.Should().BeSameAs(actor);
        actor.State.Should().Be(ActorState.Active);
    }

    [Fact]
    public async Task SendOnly_Delivers_Standard_Envelope()
    {
        await _runtime.InitializeAsync(new Dictionary<string, object>());
        var actor = await _runtime.SpawnActorAsync<ConformanceActor>("conf-send");

        await _runtime.SendMessageAsync(
            "conf-send",
            "hello",
            senderId: "tester",
            headers: new Dictionary<string, string>
            {
                [AgctorMessageHeaders.CorrelationId] = "corr-send"
            });

        var received = await actor.WaitForMessageAsync();

        received.Payload.Should().Be("hello");
        received.Headers[AgctorMessageHeaders.SenderId].Should().Be("tester");
        received.Headers[AgctorMessageHeaders.ReceiverId].Should().Be("conf-send");
        received.Headers[AgctorMessageHeaders.MessageType].Should().Be(AgctorMessageTypes.Prompt);
        received.GetCorrelationId().Should().Be("corr-send");
    }

    [Fact]
    public async Task RequestResponse_Preserves_Correlation_To_Response()
    {
        await _runtime.InitializeAsync(new Dictionary<string, object>());
        await _runtime.SpawnActorAsync<ConformanceActor>("conf-request");

        var response = await _runtime.SendMessageAsync<string>(
            "conf-request",
            "question",
            TimeSpan.FromSeconds(2),
            senderId: "tester");

        response.Should().Be("response:question");
    }

    [Fact]
    public async Task SendOnly_To_Missing_Actor_Does_Not_Throw_For_Backward_Compatibility()
    {
        await _runtime.InitializeAsync(new Dictionary<string, object>());
        DeadLetterEventArgs? deadLetter = null;
        _runtime.DeadLetter += (_, args) => deadLetter = args;

        var act = async () => await _runtime.SendMessageAsync("missing", "hello", senderId: "tester");

        await act.Should().NotThrowAsync();
        deadLetter.Should().NotBeNull();
        deadLetter!.SenderId.Should().Be("tester");
        deadLetter.ReceiverId.Should().Be("missing");
        deadLetter.MessageType.Should().Be(AgctorMessageTypes.Prompt);
        deadLetter.Payload.Should().Be("hello");
        deadLetter.Reason.Should().Be("target-actor-not-found");
    }

    [Fact]
    public async Task RequestResponse_To_Missing_Actor_Throws()
    {
        await _runtime.InitializeAsync(new Dictionary<string, object>());

        var act = async () => await _runtime.SendMessageAsync<string>(
            "missing",
            "hello",
            TimeSpan.FromMilliseconds(100),
            senderId: "tester");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class ConformanceActor : IActor
    {
        private TaskCompletionSource<IMessageEnvelope> _received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConformanceActor(string id)
        {
            Id = id;
        }

        public string Id { get; }
        public string ActorType => nameof(ConformanceActor);
        public ActorState State { get; private set; } = ActorState.Initializing;
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            State = ActorState.Active;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(ActorState.Initializing, ActorState.Active));
            return Task.CompletedTask;
        }

        public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _received.TrySetResult(envelope);
            var response = AgctorEnvelopeBuilder.Response(
                $"response:{envelope.Payload}",
                envelope,
                Id,
                AgctorMessageTypes.Result);
            return Task.FromResult<IMessageEnvelope>(response);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            State = ActorState.Stopped;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(ActorState.Active, ActorState.Stopped));
            return Task.CompletedTask;
        }

        public async Task<IMessageEnvelope> WaitForMessageAsync()
        {
            var completed = await Task.WhenAny(_received.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            completed.Should().Be(_received.Task);
            return await _received.Task;
        }
    }
}

