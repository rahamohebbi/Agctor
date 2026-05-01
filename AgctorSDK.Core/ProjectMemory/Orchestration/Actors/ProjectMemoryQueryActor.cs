using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Core.ProjectMemory.Orchestration.Actors;

/// <summary>
/// Actor boundary for the person-query LLM step. It reads context through the
/// existing guarded ProjectMemory operations and uses the shared LLM client.
/// </summary>
public sealed class ProjectMemoryQueryActor : IActor
{
    private readonly IProjectLoader _loader;
    private readonly ProjectMemoryOperations _ops;
    private readonly IProjectMemoryLlmClient _llm;
    private ActorState _state = ActorState.Initializing;

    public ProjectMemoryQueryActor(
        string id,
        IProjectLoader loader,
        ProjectMemoryOperations ops,
        IProjectMemoryLlmClient llm)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    }

    public string Id { get; }
    public string ActorType => nameof(ProjectMemoryQueryActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not ProjectMemoryQueryWorkflowRequest request)
        {
            return AgctorEnvelopeBuilder.Error(envelope, Id, $"Unsupported query payload '{envelope.Payload?.GetType().Name ?? "null"}'.");
        }

        try
        {
            var root = Path.GetFullPath(request.ProjectRoot.Trim());
            var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            var spec = ctx.AgentSpecs.FirstOrDefault(a => string.Equals(a.Id, "person-query", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("person-query agent spec missing.");
            var entityWorkspace = PersonaScenarioScope.GetEntityWorkspaceRoot(root, request.ScenarioId);
            if (!PersonaScenarioScope.IsUnderProjectRoot(root, entityWorkspace))
                throw new InvalidOperationException("Invalid scenario scope path.");

            var hits = await _ops.SearchEntitiesAsync(root, entityWorkspace, null, cancellationToken).ConfigureAwait(false);
            var profiles = new List<(string EntityKey, string Profile)>();
            foreach (var hit in hits.Take(20))
            {
                var profile = await _ops
                    .ReadDocumentAsync(spec, root, entityWorkspace, $"people/{hit.EntityKey}/profile.md", cancellationToken)
                    .ConfigureAwait(false);
                profiles.Add((hit.EntityKey, profile));
            }

            var context = ProjectMemoryPromptBuilder.BuildEntityContext(profiles);
            var prompt = ProjectMemoryPromptBuilder.BuildQueryPrompt(spec, context, request.UserMessage, request.ConversationPrefix);
            var answer = await _llm.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            return AgctorEnvelopeBuilder.Response(new ProjectMemoryQueryWorkflowResult(answer, prompt), envelope, Id, AgctorMessageTypes.Result);
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

