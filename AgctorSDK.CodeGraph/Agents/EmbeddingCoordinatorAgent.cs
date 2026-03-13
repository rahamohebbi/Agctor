using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.CodeGraph.Agents
{
    /// <summary>
    /// Coordinates embedding readiness for all semantic-search consumers.
    /// It owns lifecycle state and delegates actual indexing to IndexerAgent.
    /// </summary>
    public sealed class EmbeddingCoordinatorAgent : Agent
    {
        private string _indexerAgentId = string.Empty;
        private EmbeddingLifecycleState _state = EmbeddingLifecycleState.NotReady;
        private int _graphVersion = 1;
        private int _indexedGraphVersion;
        private DateTimeOffset? _lastIndexedAt;
        private string? _lastError;

        public EmbeddingCoordinatorAgent(string id, string indexerAgentId) : base(id)
        {
            _indexerAgentId = indexerAgentId ?? throw new ArgumentNullException(nameof(indexerAgentId));
        }

        public EmbeddingCoordinatorAgent()
        {
        }

        public void Configure(string indexerAgentId)
        {
            _indexerAgentId = indexerAgentId ?? throw new ArgumentNullException(nameof(indexerAgentId));
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            switch (envelope.Payload)
            {
                case EnsureEmbeddingsReadyMessage ensure:
                    return CreateResponseEnvelope(
                        envelope,
                        await EnsureEmbeddingsReadyAsync(ensure, cancellationToken),
                        "EmbeddingReady");

                case MarkEmbeddingsStaleMessage mark:
                    return CreateResponseEnvelope(
                        envelope,
                        HandleMarkStale(mark),
                        "EmbeddingStatus");

                case GetEmbeddingStatusMessage:
                    return CreateResponseEnvelope(
                        envelope,
                        BuildStatus(),
                        "EmbeddingStatus");
            }

            if (envelope.Headers.TryGetValue("MessageType", out var messageType) &&
                messageType == "Prompt" &&
                envelope.Payload is string prompt)
            {
                return CreateResponseEnvelope(
                    envelope,
                    await HandlePromptAsync(prompt, cancellationToken),
                    "Answer");
            }

            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        protected override async Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken)
        {
            var result = await HandlePromptAsync(prompt, cancellationToken);
            await FinalizeTask(result, cancellationToken);
        }

        protected override bool ShouldDecomposeTask(string prompt) => false;

        private async Task<EmbeddingReadyResult> EnsureEmbeddingsReadyAsync(EnsureEmbeddingsReadyMessage request, CancellationToken cancellationToken)
        {
            if (AgentFactory?.RuntimeAdapter == null)
            {
                throw new InvalidOperationException("RuntimeAdapter not available in EmbeddingCoordinatorAgent");
            }

            if (!request.ForceRefresh && IsReady())
            {
                return BuildReadyResult(triggeredIndexing: false);
            }

            _state = EmbeddingLifecycleState.Indexing;
            _lastError = null;

            try
            {
                var result = await AgentFactory.RuntimeAdapter.SendMessageAsync<string>(
                    _indexerAgentId,
                    "index",
                    timeout: TimeSpan.FromSeconds(120),
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "Prompt" },
                    cancellationToken: cancellationToken);

                if (string.IsNullOrWhiteSpace(result))
                {
                    result = "IndexingComplete";
                }

                _indexedGraphVersion = _graphVersion;
                _state = EmbeddingLifecycleState.Ready;
                _lastIndexedAt = DateTimeOffset.UtcNow;
                _lastError = null;

                return BuildReadyResult(triggeredIndexing: true);
            }
            catch (Exception ex)
            {
                _state = EmbeddingLifecycleState.Failed;
                _lastError = ex.Message;
                return new EmbeddingReadyResult(
                    IsReady: false,
                    TriggeredIndexing: true,
                    State: _state,
                    GraphVersion: _graphVersion,
                    IndexedGraphVersion: _indexedGraphVersion,
                    LastIndexedAt: _lastIndexedAt,
                    LastError: _lastError);
            }
        }

        private EmbeddingStatusResult HandleMarkStale(MarkEmbeddingsStaleMessage request)
        {
            _graphVersion++;
            _lastError = null;
            _state = _indexedGraphVersion == 0
                ? EmbeddingLifecycleState.NotReady
                : EmbeddingLifecycleState.Stale;

            LogInfo($"Marked embeddings stale. GraphVersion={_graphVersion}. Reason={request.Reason ?? "unspecified"}");
            return BuildStatus();
        }

        private async Task<string> HandlePromptAsync(string prompt, CancellationToken cancellationToken)
        {
            if (string.Equals(prompt, "index", StringComparison.OrdinalIgnoreCase))
            {
                var result = await EnsureEmbeddingsReadyAsync(
                    new EnsureEmbeddingsReadyMessage(ForceRefresh: true, Reason: "manual-index"),
                    cancellationToken);

                return result.IsReady
                    ? "Indexing complete."
                    : $"Indexing failed: {result.LastError ?? "unknown error"}";
            }

            if (string.Equals(prompt, "mark embeddings stale", StringComparison.OrdinalIgnoreCase))
            {
                var status = HandleMarkStale(new MarkEmbeddingsStaleMessage("manual"));
                return $"Embeddings marked {status.State}.";
            }

            var current = BuildStatus();
            return $"Embedding state: {current.State}. GraphVersion={current.GraphVersion}, IndexedGraphVersion={current.IndexedGraphVersion}.";
        }

        private EmbeddingReadyResult BuildReadyResult(bool triggeredIndexing)
        {
            return new EmbeddingReadyResult(
                IsReady: IsReady(),
                TriggeredIndexing: triggeredIndexing,
                State: _state,
                GraphVersion: _graphVersion,
                IndexedGraphVersion: _indexedGraphVersion,
                LastIndexedAt: _lastIndexedAt,
                LastError: _lastError);
        }

        private EmbeddingStatusResult BuildStatus()
        {
            return new EmbeddingStatusResult(
                State: _state,
                GraphVersion: _graphVersion,
                IndexedGraphVersion: _indexedGraphVersion,
                LastIndexedAt: _lastIndexedAt,
                LastError: _lastError);
        }

        private bool IsReady()
        {
            return _state == EmbeddingLifecycleState.Ready && _graphVersion == _indexedGraphVersion;
        }

        private IMessageEnvelope CreateResponseEnvelope(IMessageEnvelope request, object payload, string messageType)
        {
            var headers = new Dictionary<string, string>
            {
                ["SenderId"] = Id,
                ["ReceiverId"] = request.Headers.GetValueOrDefault("SenderId", "unknown"),
                ["MessageType"] = messageType
            };

            return new MessageEnvelope(payload, request.Metadata, Guid.NewGuid().ToString(), headers);
        }
    }
}
