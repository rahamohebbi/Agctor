using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.ProjectMemory.Orchestration.Actors;

/// <summary>
/// Actor boundary for applying already-generated extractor output. It delegates
/// to the existing ingest API so routing/projection behavior stays centralized.
/// </summary>
public sealed class ProjectMemoryIngestActor : IActor
{
    private readonly IProjectMemoryPipelineRunner _pipeline;
    private ActorState _state = ActorState.Initializing;

    public ProjectMemoryIngestActor(string id, IProjectMemoryPipelineRunner pipeline)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public string Id { get; }
    public string ActorType => nameof(ProjectMemoryIngestActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not ProjectMemoryIngestWorkflowRequest request)
        {
            return AgctorEnvelopeBuilder.Error(envelope, Id, $"Unsupported ingest payload '{envelope.Payload?.GetType().Name ?? "null"}'.");
        }

        try
        {
            var result = await _pipeline
                .IngestFromExtractorOutputAsync(request.ProjectRoot, request.ScenarioId, request.RawExtractorLlmText, cancellationToken)
                .ConfigureAwait(false);
            return AgctorEnvelopeBuilder.Response(new ProjectMemoryIngestWorkflowResult(result), envelope, Id, AgctorMessageTypes.Result);
        }
        catch (Exception ex)
        {
            return AgctorEnvelopeBuilder.Error(envelope, Id, ex.Message, ex);
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Stopped, "Shutdown");
        return Task.CompletedTask;
    }

    private void ChangeState(ActorState newState, string reason)
    {
        var previous = _state;
        if (previous == newState) return;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previous, newState, reason));
    }
}

