using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Sessions.Messages;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Routes session commands to the corresponding per-session memory actor.
    /// </summary>
    public sealed class SessionCoordinatorAgent : Agent
    {
        private readonly ISessionStore _store;
        private readonly ISessionContextComposer _composer;
        private readonly SessionMemoryOptions _options;
        private readonly Dictionary<string, string> _memoryActorIds = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SessionCoordinatorAgent(
            string id,
            ISessionStore store,
            ISessionContextComposer composer,
            SessionMemoryOptions options) : base(id)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope?.Payload == null)
            {
                return await base.ReceiveAsync(envelope, cancellationToken);
            }

            object payload = envelope.Payload;
            object result;

            switch (payload)
            {
                case CreateSessionMessage create:
                    result = await _store.CreateSessionAsync(create.SessionId, create.Title, projectId: null, cancellationToken);
                    break;

                case ListSessionsMessage list:
                    var sessions = await _store.ListSessionsAsync(list.Limit, list.Offset, cancellationToken);
                    result = new SessionListResult { Sessions = sessions };
                    break;

                case GetSessionTranscriptMessage transcriptReq:
                    result = await RouteToMemoryActorAsync(transcriptReq.SessionId, transcriptReq, cancellationToken);
                    break;

                case GetSessionContextMessage contextReq:
                    result = await RouteToMemoryActorAsync(contextReq.SessionId, contextReq, cancellationToken);
                    break;

                case AppendSessionTurnMessage append:
                    if (string.IsNullOrWhiteSpace(append.Turn.SessionId))
                    {
                        throw new InvalidOperationException("AppendSessionTurnMessage requires a non-empty SessionId.");
                    }
                    result = await RouteToMemoryActorAsync(append.Turn.SessionId, append, cancellationToken);
                    break;

                case UpsertSessionTraceLinkMessage upsertTrace:
                    if (string.IsNullOrWhiteSpace(upsertTrace.TraceLink.SessionId))
                    {
                        throw new InvalidOperationException("UpsertSessionTraceLinkMessage requires a non-empty SessionId.");
                    }
                    result = await RouteToMemoryActorAsync(upsertTrace.TraceLink.SessionId, upsertTrace, cancellationToken);
                    break;

                case GetSessionTraceLinksMessage getTraceLinks:
                    result = await RouteToMemoryActorAsync(getTraceLinks.SessionId, getTraceLinks, cancellationToken);
                    break;

                case GetSessionTraceLinkByTurnMessage traceByTurn:
                    result = await RouteToMemoryActorAsync(traceByTurn.SessionId, traceByTurn, cancellationToken);
                    break;

                default:
                    return await base.ReceiveAsync(envelope, cancellationToken);
            }

            return CreateResponseEnvelope(envelope, result);
        }

        private async Task<object> RouteToMemoryActorAsync(string sessionId, object command, CancellationToken cancellationToken)
        {
            var actorId = await EnsureMemoryActorAsync(sessionId, cancellationToken);
            if (AgentFactory?.RuntimeAdapter == null)
            {
                throw new InvalidOperationException("RuntimeAdapter is required for session memory routing.");
            }

            return command switch
            {
                GetSessionContextMessage => await AgentFactory.RuntimeAdapter.SendMessageAsync<SessionContextPackage>(
                    actorId,
                    command,
                    timeout: TimeSpan.FromSeconds(20),
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "SessionCommand" },
                    cancellationToken: cancellationToken),
                GetSessionTranscriptMessage => await AgentFactory.RuntimeAdapter.SendMessageAsync<SessionTranscript>(
                    actorId,
                    command,
                    timeout: TimeSpan.FromSeconds(20),
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "SessionCommand" },
                    cancellationToken: cancellationToken),
                AppendSessionTurnMessage => await AgentFactory.RuntimeAdapter.SendMessageAsync<SessionTurn>(
                    actorId,
                    command,
                    timeout: TimeSpan.FromSeconds(20),
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "SessionCommand" },
                    cancellationToken: cancellationToken),
                UpsertSessionTraceLinkMessage => await AgentFactory.RuntimeAdapter.SendMessageAsync<SessionTraceLink>(
                    actorId,
                    command,
                    timeout: TimeSpan.FromSeconds(20),
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "SessionCommand" },
                    cancellationToken: cancellationToken),
                GetSessionTraceLinksMessage => await AgentFactory.RuntimeAdapter.SendMessageAsync<IReadOnlyList<SessionTraceLink>>(
                    actorId,
                    command,
                    timeout: TimeSpan.FromSeconds(20),
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "SessionCommand" },
                    cancellationToken: cancellationToken),
                GetSessionTraceLinkByTurnMessage => await AgentFactory.RuntimeAdapter.SendMessageAsync<SessionTraceLink>(
                    actorId,
                    command,
                    timeout: TimeSpan.FromSeconds(20),
                    senderId: Id,
                    headers: new Dictionary<string, string> { ["MessageType"] = "SessionCommand" },
                    cancellationToken: cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported command type '{command.GetType().Name}'.")
            };
        }

        private async Task<string> EnsureMemoryActorAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("sessionId is required.");
            }

            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_memoryActorIds.TryGetValue(sessionId, out var existingActorId))
                {
                    return existingActorId;
                }

                if (AgentFactory?.RuntimeAdapter == null)
                {
                    throw new InvalidOperationException("RuntimeAdapter is required to spawn SessionMemoryAgent.");
                }

                var actorId = $"session-memory-{sessionId}";
                await AgentFactory.RuntimeAdapter.SpawnActorAsync<SessionMemoryAgent>(
                    actorId,
                    id => new SessionMemoryAgent(id, sessionId, _store, _composer, _options),
                    initializationData: null,
                    cancellationToken: cancellationToken);

                _memoryActorIds[sessionId] = actorId;
                return actorId;
            }
            finally
            {
                _lock.Release();
            }
        }

        private IMessageEnvelope CreateResponseEnvelope(IMessageEnvelope request, object payload)
        {
            var headers = new Dictionary<string, string>
            {
                ["SenderId"] = Id,
                ["ReceiverId"] = request.Headers.GetValueOrDefault("SenderId", "unknown"),
                ["MessageType"] = "Result"
            };
            var metadata = new Dictionary<string, object>
            {
                ["Timestamp"] = DateTimeOffset.UtcNow
            };
            if (request.Metadata.TryGetValue("CorrelationId", out var corr))
            {
                metadata["CorrelationId"] = corr;
            }

            return new MessageEnvelope(payload, metadata, null, headers);
        }
    }
}
