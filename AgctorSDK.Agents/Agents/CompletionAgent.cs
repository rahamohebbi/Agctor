using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Completion;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Actor that turns a <see cref="CompletionRequest"/> into text.
    /// It owns no product rules. Callers pass the prompt and an optional fallback so a
    /// missing model never faults this actor or blocks the parent.
    /// </summary>
    public sealed class CompletionAgent : BaseActor
    {
        private readonly ITextGenerator _generator;

        public CompletionAgent(string id, ITextGenerator generator)
            : base(id, nameof(CompletionAgent))
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is not CompletionRequest request)
            {
                return ActorReply.With(new TextGenerationResult(false, string.Empty, Name, "Expected CompletionRequest."));
            }

            TextGenerationResult result;
            try
            {
                result = await _generator.GenerateAsync(
                    new TextGenerationRequest(request.Prompt, request.SystemPrompt),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new TextGenerationResult(false, string.Empty, Name, ex.Message);
            }

            if (!result.Succeeded && !string.IsNullOrWhiteSpace(request.FallbackText))
            {
                return ActorReply.With(new TextGenerationResult(true, request.FallbackText!, "fallback", result.Error));
            }

            return ActorReply.With(result);
        }

        private string Name => ActorType + ":" + Id;
    }
}
