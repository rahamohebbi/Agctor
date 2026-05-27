using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.Core.ProjectMemory.Companion.Actors;

/// <summary>
/// On session checkpoint/delete, ingests only transcript turns after the last stored
/// <see cref="SessionSummary.LastIncludedSequence"/> through the ProjectMemory pipeline.
/// </summary>
public sealed class SessionEndIngestActor : IActor
{
    private const int MaxSummaryChars = 4000;
    private const string IngestPreamble =
        "[Session end — extract durable facts and timeline observations from this conversation segment]\n";

    private readonly ISessionStore _sessions;
    private readonly IProjectMemoryPipelineRunner _pipeline;
    private readonly IVisualPipelineService? _visualPipeline;
    private readonly VisualAssetCatalogStore? _visualCatalog;
    private ActorState _state = ActorState.Initializing;

    public SessionEndIngestActor(
        string id,
        ISessionStore sessions,
        IProjectMemoryPipelineRunner pipeline,
        IVisualPipelineService? visualPipeline = null,
        VisualAssetCatalogStore? visualCatalog = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Actor id is required.", nameof(id)) : id;
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _visualPipeline = visualPipeline;
        _visualCatalog = visualCatalog;
    }

    public string Id { get; }
    public string ActorType => nameof(SessionEndIngestActor);
    public ActorState State => _state;
    public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(ActorState.Active, "Initialized");
        return Task.CompletedTask;
    }

    public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not SessionEndIngestWorkflowRequest request)
        {
            return AgctorEnvelopeBuilder.Error(
                envelope,
                Id,
                $"Unsupported session-end ingest payload '{envelope.Payload?.GetType().Name ?? "null"}'.");
        }

        try
        {
            var result = await RunAsync(request, cancellationToken).ConfigureAwait(false);
            return AgctorEnvelopeBuilder.Response(result, envelope, Id, AgctorMessageTypes.Result);
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

    private async Task<SessionEndIngestWorkflowResult> RunAsync(
        SessionEndIngestWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return Skipped("session_id_required");

        var session = await _sessions.GetSessionAsync(request.SessionId.Trim(), cancellationToken).ConfigureAwait(false);
        if (session == null)
            return Skipped("session_not_found");

        var scenarioId = await ResolveScenarioIdAsync(request, session, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(scenarioId))
            return Skipped("no_scenario");

        var turns = await _sessions.GetTurnsAsync(request.SessionId.Trim(), null, cancellationToken).ConfigureAwait(false);
        var summary = await _sessions.GetSummaryAsync(request.SessionId.Trim(), cancellationToken).ConfigureAwait(false);
        var since = summary?.LastIncludedSequence ?? 0;
        var newTurns = turns
            .Where(t => t.Sequence > since && t.Role is SessionRole.User or SessionRole.Assistant)
            .ToList();
        if (newTurns.Count == 0)
            return Skipped("no_new_turns");

        var prefix = SessionTranscriptFormatter.BuildPrefix(newTurns);
        if (string.IsNullOrWhiteSpace(prefix))
            return Skipped("no_transcript_content");

        var pipelineRequest = new ProjectMemoryPipelineRequest
        {
            ProjectRoot = request.ProjectRoot,
            UserMessage = IngestPreamble + prefix,
            CorrelationId = Guid.NewGuid().ToString("N"),
            Mode = ProjectMemoryPipelineMode.IngestOnly,
            ScenarioId = scenarioId,
            SessionId = request.SessionId.Trim()
        };

        var pipelineResult = await _pipeline.RunAsync(pipelineRequest, cancellationToken).ConfigureAwait(false);
        await ReconcileSessionAttachmentsAsync(
                request,
                scenarioId,
                session,
                newTurns,
                cancellationToken)
            .ConfigureAwait(false);
        var maxSeq = newTurns.Max(t => t.Sequence);
        var snippet = Truncate(pipelineResult.FinalText, MaxSummaryChars);

        await _sessions.UpsertSummaryAsync(
            new SessionSummary
            {
                SessionId = request.SessionId.Trim(),
                Content = snippet,
                LastIncludedSequence = maxSeq,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            cancellationToken).ConfigureAwait(false);

        return new SessionEndIngestWorkflowResult(
            pipelineResult.Success,
            Skipped: false,
            SkipReason: null,
            pipelineResult.CorrelationId,
            snippet,
            maxSeq);
    }

    private async Task ReconcileSessionAttachmentsAsync(
        SessionEndIngestWorkflowRequest request,
        string scenarioId,
        SessionInfo session,
        IReadOnlyList<SessionTurn> newTurns,
        CancellationToken cancellationToken)
    {
        if (_visualPipeline == null || _visualCatalog == null)
            return;

        var assetIds = new List<string>();
        string? userMessage = null;
        foreach (var turn in newTurns)
        {
            var env = SessionAttachmentJson.Deserialize(turn.AttachmentsJson);
            if (env != null)
            {
                foreach (var att in env.Attachments)
                {
                    if (!string.IsNullOrWhiteSpace(att.AssetId))
                        assetIds.Add(att.AssetId.Trim());
                }
            }

            if (turn.Role == SessionRole.User && !string.IsNullOrWhiteSpace(turn.Content))
                userMessage = turn.Content;
        }

        if (assetIds.Count == 0)
            return;

        var pending = new List<string>();
        foreach (var id in assetIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var record = await _visualCatalog
                .LoadAsync(request.ProjectRoot, scenarioId, id, cancellationToken)
                .ConfigureAwait(false);
            if (record != null && NeedsSessionEndExtract(record))
                pending.Add(id);
        }

        if (pending.Count == 0)
            return;

        string? focus = null;
        if (!string.IsNullOrWhiteSpace(session.ProjectId))
        {
            var project = await _sessions.GetProjectAsync(session.ProjectId, cancellationToken).ConfigureAwait(false);
            focus = project?.FocusEntityKey;
        }

        _visualPipeline.QueueExtractForAssets(
            request.ProjectRoot,
            scenarioId,
            pending,
            userMessage,
            focus);
    }

    private static bool NeedsSessionEndExtract(VisualAssetRecord record)
    {
        var state = record.State ?? string.Empty;
        return string.Equals(state, VisualAssetStates.Uploaded, StringComparison.OrdinalIgnoreCase)
               || string.Equals(state, VisualAssetStates.ReadyForExtract, StringComparison.OrdinalIgnoreCase)
               || string.Equals(state, VisualAssetStates.Ready, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> ResolveScenarioIdAsync(
        SessionEndIngestWorkflowRequest request,
        SessionInfo session,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ScenarioId))
            return request.ScenarioId.Trim();

        if (string.IsNullOrWhiteSpace(session.ProjectId))
            return null;

        var project = await _sessions.GetProjectAsync(session.ProjectId, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(project?.ScenarioId) ? null : project.ScenarioId.Trim();
    }

    private static SessionEndIngestWorkflowResult Skipped(string reason) =>
        new(false, true, reason, null, null, 0);

    private static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text ?? string.Empty;
        return text[..maxChars];
    }

    private void ChangeState(ActorState newState, string reason)
    {
        var previous = _state;
        if (previous == newState) return;
        _state = newState;
        StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previous, newState, reason));
    }
}
