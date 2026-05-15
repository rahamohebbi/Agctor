using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Utils.Logging;

namespace AgctorSDK.Core.Tools.Implementations;

/// <summary>
/// Base class for tool actors: <see cref="IActor"/> + <see cref="IToolActor"/> without <see cref="IAgent"/>.
/// Agents invoke tools via <see cref="IAgentFactory.InvokeToolByPromptAsync"/> or <see cref="ToolRequest"/> messages;
/// tools must not spawn or message other agents except returning results to the caller's request-response channel.
/// </summary>
public abstract class ToolActorBase : BaseActor, IToolActor
{
    protected IAgctorLogger Logger { get; }

    protected ToolActorBase(string id, string actorType) : base(id, actorType)
    {
        Logger = LoggerFactory.CreateLogger(actorType);
    }

    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        switch (envelope.Payload)
        {
            case ProcessPromptMessage pm:
                var tr = await OnProcessPromptAsync(pm.Prompt, cancellationToken).ConfigureAwait(false);
                return new MessageEnvelope(tr);
            case ToolRequest req:
                var r = await Handle(req).ConfigureAwait(false);
                return new MessageEnvelope(r);
            default:
                return new MessageEnvelope(new ToolResult
                {
                    IsSuccess = false,
                    Error = $"Unsupported payload: {envelope.Payload?.GetType().Name ?? "null"}"
                });
        }
    }

    /// <summary>
    /// Parses a natural-language or CLI-style prompt and returns a <see cref="ToolResult"/> (no agent hierarchy).
    /// </summary>
    protected abstract Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a CLI-style prompt for tests or direct callers without going through the actor runtime mailbox.
    /// </summary>
    public Task<ToolResult> RunFromPromptAsync(string prompt, CancellationToken cancellationToken = default) =>
        OnProcessPromptAsync(prompt, cancellationToken);

    public abstract Task<ToolResult> Handle(ToolRequest request);

    protected void LogInfo(string message) => Logger.Info(message);
    protected void LogWarning(string message) => Logger.Warning("{0}", message);
    protected void LogError(string message) => Logger.Error("{0}", message);
}
