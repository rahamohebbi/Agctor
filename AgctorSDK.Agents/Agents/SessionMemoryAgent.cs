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
    /// Owns one session transcript and summary state.
    /// </summary>
    public sealed class SessionMemoryAgent : Agent
    {
        private readonly ISessionStore _store;
        private readonly ISessionContextComposer _composer;
        private readonly SessionMemoryOptions _options;
        private readonly string _sessionId;

        public SessionMemoryAgent(
            string id,
            string sessionId,
            ISessionStore store,
            ISessionContextComposer composer,
            SessionMemoryOptions options) : base(id)
        {
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
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
                case AppendSessionTurnMessage append:
                    if (!string.Equals(append.Turn.SessionId, _sessionId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Turn session '{append.Turn.SessionId}' does not match memory actor session '{_sessionId}'.");
                    }
                    result = await _store.AppendTurnAsync(append.Turn, cancellationToken);
                    break;

                case UpsertSessionTraceLinkMessage upsertTrace:
                    if (!string.Equals(upsertTrace.TraceLink.SessionId, _sessionId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Trace link session '{upsertTrace.TraceLink.SessionId}' does not match memory actor session '{_sessionId}'.");
                    }
                    result = await _store.UpsertTraceLinkAsync(upsertTrace.TraceLink, cancellationToken);
                    break;

                case GetSessionTraceLinksMessage traceLinksReq:
                    EnsureSession(traceLinksReq.SessionId);
                    result = await _store.GetTraceLinksAsync(_sessionId, cancellationToken);
                    break;

                case GetSessionTraceLinkByTurnMessage traceByTurnReq:
                    EnsureSession(traceByTurnReq.SessionId);
                    result = await _store.GetTraceLinkByTurnIdAsync(_sessionId, traceByTurnReq.TurnId, cancellationToken);
                    break;

                case GetSessionTranscriptMessage transcriptReq:
                    EnsureSession(transcriptReq.SessionId);
                    var session = await _store.GetSessionAsync(_sessionId, cancellationToken)
                                  ?? throw new InvalidOperationException($"Session '{_sessionId}' does not exist.");
                    var turns = await _store.GetTurnsAsync(_sessionId, transcriptReq.LastTurns, cancellationToken);
                    var traceLinks = await _store.GetTraceLinksAsync(_sessionId, cancellationToken);
                    var summary = await _store.GetSummaryAsync(_sessionId, cancellationToken);
                    result = new SessionTranscript
                    {
                        Session = session,
                        Turns = turns,
                        TraceLinks = traceLinks,
                        Summary = summary
                    };
                    break;

                case GetSessionContextMessage contextReq:
                    EnsureSession(contextReq.SessionId);
                    var ctxSession = await _store.GetSessionAsync(_sessionId, cancellationToken)
                                    ?? throw new InvalidOperationException($"Session '{_sessionId}' does not exist.");
                    var ctxTurns = await _store.GetTurnsAsync(_sessionId, _options.RecentTurnWindow, cancellationToken);
                    var ctxSummary = await _store.GetSummaryAsync(_sessionId, cancellationToken);
                    var transcript = new SessionTranscript
                    {
                        Session = ctxSession,
                        Turns = ctxTurns,
                        Summary = ctxSummary
                    };
                    result = _composer.Compose(transcript, contextReq.CurrentPrompt, _options);
                    break;

                case CreateSessionMessage:
                    result = await _store.GetSessionAsync(_sessionId, cancellationToken)
                             ?? await _store.CreateSessionAsync(_sessionId, title: null, projectId: null, cancellationToken);
                    break;

                default:
                    return await base.ReceiveAsync(envelope, cancellationToken);
            }

            return CreateResponseEnvelope(envelope, result);
        }

        private static IMessageEnvelope CreateResponseEnvelope(IMessageEnvelope request, object payload)
        {
            var headers = new Dictionary<string, string>
            {
                ["SenderId"] = request.Headers.GetValueOrDefault("ReceiverId", "session-memory-agent"),
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

        private void EnsureSession(string sessionId)
        {
            if (!string.Equals(sessionId, _sessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Session '{sessionId}' is not owned by this memory actor '{_sessionId}'.");
            }
        }
    }
}
