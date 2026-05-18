using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Utils.Logging;

namespace AgctorSDK.Core.Tools;

/// <summary>
/// Base for tool actors: <see cref="IActor"/> + <see cref="IToolActor"/> without <see cref="IAgent"/>.
/// Tools must not spawn agents; they only handle prompts and <see cref="ToolRequest"/> messages.
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

    protected abstract Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken);

    public Task<ToolResult> RunFromPromptAsync(string prompt, CancellationToken cancellationToken = default) =>
        OnProcessPromptAsync(prompt, cancellationToken);

    public abstract Task<ToolResult> Handle(ToolRequest request);

    protected void LogInfo(string message) => Logger.Info(message);
    protected void LogWarning(string message) => Logger.Warning("{0}", message);
    protected void LogError(string message) => Logger.Error("{0}", message);
}
