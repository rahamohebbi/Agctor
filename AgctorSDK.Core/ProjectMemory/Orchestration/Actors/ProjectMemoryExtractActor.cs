using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Loading;

namespace AgctorSDK.Core.ProjectMemory.Orchestration.Actors;

/// <summary>
/// Actor boundary for the person-extractor LLM step. It owns orchestration for
/// extraction while the loaded YAML spec remains the source of instructions.
/// </summary>
public sealed class ProjectMemoryExtractActor : IActor
{
    private readonly IProjectLoader _loader;
    private readonly IProjectMemoryLlmClient _llm;
    private ActorState _state = ActorState.Initializing;

    public ProjectMemoryExtractActor(string id, IProjectLoader loader, IProjectMemoryLlmClient llm)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    }

    public string Id { get; }
    public string ActorType => nameof(ProjectMemoryExtractActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not ProjectMemoryExtractWorkflowRequest request)
        {
            return AgctorEnvelopeBuilder.Error(envelope, Id, $"Unsupported extract payload '{envelope.Payload?.GetType().Name ?? "null"}'.");
        }

        try
        {
            var root = Path.GetFullPath(request.ProjectRoot.Trim());
            var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            var spec = ctx.AgentSpecs.FirstOrDefault(a => string.Equals(a.Id, "person-extractor", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("person-extractor agent spec missing.");
            var prompt = ProjectMemoryPromptBuilder.BuildExtractPrompt(spec, request.UserMessage, request.ConversationPrefix);
            var raw = await _llm.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            return AgctorEnvelopeBuilder.Response(new ProjectMemoryExtractWorkflowResult(raw, prompt), envelope, Id, AgctorMessageTypes.Result);
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

