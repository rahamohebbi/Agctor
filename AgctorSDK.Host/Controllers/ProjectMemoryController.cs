using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using System.Net.Http.Json;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Coref;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Core.Streaming;
using AgctorSDK.Core.ProjectMemory.Indexing;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Validation;
using AgctorSDK.Core.ProjectMemory.Yaml;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.ProjectMemory;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Core.Utils.ActivityTracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// Dashboard API for portable project memory (PRD-013 UX): agents, schema, validate, rebuild, workspace tree.
/// </summary>
[ApiController]
[Route("api/project-memory")]
[Produces("application/json")]
public sealed class ProjectMemoryController : ControllerBase
{
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions> _options;
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;
    private readonly IProjectMemoryFileService _files;
    private readonly RebuildCoordinator _rebuild;
    private readonly IUserProjectMemorySettingsService _userProjectRoot;
    private readonly IWebHostEnvironment _env;
    private readonly ISessionStore _sessions;
    private readonly IProjectMemoryPipelineRunner _pipeline;
    private readonly IProjectMemoryAgentYamlPersistence _agentYaml;
    private readonly IProjectMemoryPersonaLlmRunner _personaLlmRunner;
    private readonly IScenarioCatalog _scenarioCatalog;
    private readonly IScenarioFlowRouterLlmService _scenarioFlowRouterLlm;
    private readonly IActivityTracker? _activityTracker;
    private readonly ILogger<ProjectMemoryController> _logger;
    private static readonly HttpClient LlmHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonSse = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IConfirmationIntentClassifier _confirmClassifier;
    private readonly IGenericInboxReplayService _genericInboxReplay;
    private readonly IProjectMemoryCoreferenceCoordinator _corefCoordinator;

    public ProjectMemoryController(
        IOptionsMonitor<ProjectMemoryAgentOptions> options,
        IProjectLoader loader,
        IEntityRegistry entities,
        IProjectMemoryFileService files,
        RebuildCoordinator rebuild,
        IUserProjectMemorySettingsService userProjectRoot,
        IWebHostEnvironment env,
        ISessionStore sessions,
        IProjectMemoryPipelineRunner pipeline,
        IProjectMemoryAgentYamlPersistence agentYaml,
        IProjectMemoryPersonaLlmRunner personaLlmRunner,
        IScenarioCatalog scenarioCatalog,
        IScenarioFlowRouterLlmService scenarioFlowRouterLlm,
        IConfirmationIntentClassifier confirmClassifier,
        IGenericInboxReplayService genericInboxReplay,
        IProjectMemoryCoreferenceCoordinator corefCoordinator,
        ILogger<ProjectMemoryController> logger,
        IActivityTracker? activityTracker = null)
    {
        _options = options;
        _loader = loader;
        _entities = entities;
        _files = files;
        _rebuild = rebuild;
        _userProjectRoot = userProjectRoot;
        _env = env;
        _sessions = sessions;
        _pipeline = pipeline;
        _agentYaml = agentYaml;
        _personaLlmRunner = personaLlmRunner;
        _scenarioCatalog = scenarioCatalog;
        _scenarioFlowRouterLlm = scenarioFlowRouterLlm;
        _confirmClassifier = confirmClassifier;
        _genericInboxReplay = genericInboxReplay;
        _corefCoordinator = corefCoordinator;
        _activityTracker = activityTracker;
        _logger = logger;
    }

    private string? RootOrNull()
    {
        var r = _options.CurrentValue.ProjectRoot?.Trim();
        return string.IsNullOrEmpty(r) ? null : Path.GetFullPath(r);
    }

    private ActionResult BadRoot() =>
        BadRequest(new { error = "Agctor:ProjectMemory:ProjectRoot is not set. Use Maintenance page or appsettings." });

    private async Task<(LoadedProjectContext? Ctx, ActionResult? Error)> TryLoadContextAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            return (ctx, null);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Project memory directory layout invalid for root {Root}", root);
            return (null, BadRequest(new { error = ex.Message }));
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Project memory file missing for root {Root}", root);
            return (null, BadRequest(new { error = ex.Message }));
        }
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(ProjectMemoryStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectMemoryStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        var sampleDefault = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "samples", "people-project"));
        var dto = new ProjectMemoryStatusDto
        {
            ProjectRoot = root ?? "",
            DefaultSampleProjectRoot = sampleDefault
        };
        if (root != null)
            dto.UsesDefaultSampleProjectRoot = string.Equals(Path.GetFullPath(root), sampleDefault, StringComparison.OrdinalIgnoreCase);
        if (root == null)
        {
            dto.Error = "Project root not configured.";
            return Ok(dto);
        }

        try
        {
            var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            dto.ProjectLoaded = true;
            dto.ProjectId = ctx.Project.ProjectId;
            dto.ProjectType = ctx.Project.ProjectType;
            dto.RuntimeMode = ctx.Runtime.Mode;
            dto.AgentCount = ctx.AgentSpecs.Count;
        }
        catch (Exception ex)
        {
            dto.Error = ex.Message;
            _logger.LogWarning(ex, "Project memory status load failed");
        }

        return Ok(dto);
    }

    [HttpGet("agents")]
    public async Task<ActionResult<IReadOnlyList<AgentListItemDto>>> ListAgents(CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var (ctx, err) = await TryLoadContextAsync(root, cancellationToken).ConfigureAwait(false);
        if (err != null || ctx == null)
            return err!;
        var list = ctx.AgentSpecs.Select(a => new AgentListItemDto
        {
            Id = a.Id,
            Name = a.Name,
            Role = a.Role,
            Description = a.Description ?? "",
            InputType = a.Input?.Type ?? "",
            OutputType = a.Output?.Type ?? "",
            ToolsAllow = a.Tools.Allow,
            ToolsDeny = a.Tools.Deny,
            MemoryRead = a.MemoryAccess.Read,
            MemoryWrite = a.MemoryAccess.Write,
            Guardrails = a.Guardrails,
            ProjectTypes = a.ProjectTypes,
            SourcePath = a.SourcePath,
            RelativePath = a.SourcePath != null ? ProjectMemoryPathSecurity.ToRelativePath(root, a.SourcePath) : null
        }).ToList();
        return Ok(list);
    }

    [HttpGet("agents/{id}")]
    public async Task<ActionResult<AgentDetailDto>> GetAgent(string id, CancellationToken cancellationToken)
    {
        var r = await _agentYaml.GetAgentDetailAsync(id, cancellationToken).ConfigureAwait(false);
        return r.StatusCode switch
        {
            200 => Ok(r.Data!),
            404 => NotFound(r.Error),
            _ => StatusCode(r.StatusCode, r.Error)
        };
    }

    [HttpPut("agents/{id}")]
    public async Task<ActionResult> SaveAgent(string id, [FromBody] SaveAgentRequestDto body, CancellationToken cancellationToken)
    {
        var r = await _agentYaml.SaveAgentAsync(id, body, createOnly: false, cancellationToken).ConfigureAwait(false);
        return r.StatusCode switch
        {
            200 => Ok(r.Data),
            _ => StatusCode(r.StatusCode, r.Error)
        };
    }

    [HttpDelete("agents/{id}")]
    public async Task<ActionResult> DeleteAgent(string id, CancellationToken cancellationToken)
    {
        var r = await _agentYaml.DeleteAgentAsync(id, cancellationToken).ConfigureAwait(false);
        return r.StatusCode switch
        {
            200 => Ok(r.Data),
            404 => NotFound(r.Error),
            _ => StatusCode(r.StatusCode, r.Error)
        };
    }

    [HttpGet("templates")]
    public ActionResult<IReadOnlyList<AgentTemplateDto>> GetTemplates() => Ok(LoadTemplates());

    [HttpPost("playground/run")]
    public async Task<ActionResult<ProjectMemoryPlaygroundRunResponseDto>> RunPlayground(
        [FromBody] ProjectMemoryPlaygroundRunRequestDto body,
        CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();
        if (string.IsNullOrWhiteSpace(body.AgentId))
            return BadRequest(new { error = "agentId is required." });
        if (string.IsNullOrWhiteSpace(body.InputText))
            return BadRequest(new { error = "inputText is required." });

        var (ctx, err) = await TryLoadContextAsync(root, cancellationToken).ConfigureAwait(false);
        if (err != null || ctx == null)
            return err!;
        var spec = ctx.AgentSpecs.FirstOrDefault(a => string.Equals(a.Id, body.AgentId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (spec == null)
            return NotFound(new { error = $"Agent spec '{body.AgentId}' not found in project memory." });

        var sw = Stopwatch.StartNew();
        var run = await _personaLlmRunner.RunAsync(
                root,
                body.SessionId,
                body.AgentId,
                body.InputText,
                cancellationToken,
                scenarioId: body.ScenarioId?.Trim())
            .ConfigureAwait(false);
        sw.Stop();
        if (!run.Ok)
            return BadRequest(new { error = run.ErrorMessage ?? "Playground run failed." });

        var output = run.OutputText ?? "";
        var looksJson = LooksLikeJson(output, out var jsonErr);
        return Ok(new ProjectMemoryPlaygroundRunResponseDto
        {
            AgentId = spec.Id,
            AgentName = string.IsNullOrWhiteSpace(spec.Name) ? spec.Id : spec.Name,
            OutputText = output,
            OutputLooksLikeJson = looksJson,
            JsonValidationError = jsonErr,
            ElapsedMs = sw.ElapsedMilliseconds
        });
    }

    /// <summary>
    /// Runs the project-memory pipeline (extract → route/write → optional person-query). Writes canonical files when ingest succeeds; unlike playground, this applies intents to disk.
    /// </summary>
    [HttpPost("orchestrator/run")]
    [ProducesResponseType(typeof(ProjectMemoryOrchestratorRunResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectMemoryOrchestratorRunResponseDto>> RunOrchestrator(
        [FromBody] ProjectMemoryOrchestratorRunRequestDto body,
        CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();
        if (string.IsNullOrWhiteSpace(body.UserMessage))
            return BadRequest(new { error = "userMessage is required." });
        if (!TryParsePipelineMode(body.Mode, out var mode))
            return BadRequest(new { error = "Invalid mode. Use auto, ingestOnly, or queryOnly." });

        string? conversationPrefix = null;
        if (!string.IsNullOrWhiteSpace(body.SessionId))
        {
            var prior = await _sessions.GetTurnsAsync(body.SessionId.Trim(), null, cancellationToken).ConfigureAwait(false);
            conversationPrefix = BuildOrchestratorTranscriptPrefix(prior);
        }

        var req = new ProjectMemoryPipelineRequest
        {
            ProjectRoot = root,
            UserMessage = body.UserMessage.Trim(),
            CorrelationId = body.CorrelationId?.Trim() ?? "",
            Mode = mode,
            ConversationPrefix = conversationPrefix,
            ScenarioId = body.ScenarioId?.Trim(),
            SessionId = body.SessionId?.Trim(), // PRD-018: tags resolver mentions with the session.
        };

        var result = await _pipeline.RunAsync(req, cancellationToken).ConfigureAwait(false);
        return Ok(new ProjectMemoryOrchestratorRunResponseDto
        {
            CorrelationId = result.CorrelationId,
            Success = result.Success,
            FinalText = ProjectMemoryUiLinkFormatter.WithAbsoluteWorkspaceLinks(result.FinalText, Request),
            Steps = result.Steps.Select(s => new ProjectMemoryOrchestratorStepDto
            {
                Name = s.Name,
                Ok = s.Ok,
                Detail = s.Detail,
                UpdatedFiles = s.UpdatedFiles
            }).ToList()
        });
    }

    /// <summary>
    /// PRD-019 back-fill: replay <c>confirmed.yaml</c> through current <c>routing-rules.yaml</c> and project routed
    /// rows into entity files. Idempotent unless <c>includeAlreadyReplayed = true</c>.
    /// </summary>
    [HttpPost("generic-inbox/replay")]
    [ProducesResponseType(typeof(ProjectMemoryGenericInboxReplayResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectMemoryGenericInboxReplayResponseDto>> ReplayGenericInbox(
        [FromBody] ProjectMemoryGenericInboxReplayRequestDto? body,
        CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var options = new GenericInboxReplayOptions
        {
            IncludeAlreadyReplayed = body?.IncludeAlreadyReplayed ?? false,
            OnlyEntityKeys = body?.OnlyEntityKeys,
            OnlyKnowledgeTypes = body?.OnlyKnowledgeTypes
        };

        var report = await _genericInboxReplay
            .ReplayAsync(root, body?.ScenarioId, options, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new ProjectMemoryGenericInboxReplayResponseDto
        {
            Considered = report.Considered,
            Routed = report.Routed,
            SkippedAlreadyReplayed = report.SkippedAlreadyReplayed,
            SkippedRouteMiss = report.SkippedRouteMiss,
            SkippedUnresolvedEntity = report.SkippedUnresolvedEntity,
            UpdatedFiles = report.UpdatedFiles,
            Issues = report.Issues.Select(i => new ProjectMemoryGenericInboxReplayIssueDto
            {
                Code = i.Code,
                Message = i.Message,
                IsError = i.IsError
            }).ToList()
        });
    }

    /// <summary>Returns the most recent <c>Assistant</c> turn content (used to ground confirmation classification).</summary>
    private static string? LastAssistantContent(IReadOnlyList<SessionTurn>? turns)
    {
        if (turns == null || turns.Count == 0)
            return null;
        for (var i = turns.Count - 1; i >= 0; i--)
        {
            var t = turns[i];
            if (t.Role == SessionRole.Assistant && !string.IsNullOrWhiteSpace(t.Content))
                return t.Content;
        }

        return null;
    }

    private static string? BuildOrchestratorTranscriptPrefix(IReadOnlyList<SessionTurn>? turns)
    {
        if (turns == null || turns.Count == 0)
            return null;
        var sb = new StringBuilder();
        foreach (var t in turns.OrderBy(x => x.Sequence))
        {
            if (t.Role is SessionRole.System or SessionRole.Tool)
                continue;
            var label = t.Role == SessionRole.User ? "User" : "Assistant";
            sb.Append(label).Append(": ").Append(t.Content).Append('\n');
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static bool TryParsePipelineMode(string? s, out ProjectMemoryPipelineMode mode)
    {
        mode = ProjectMemoryPipelineMode.Auto;
        if (string.IsNullOrWhiteSpace(s))
            return true;
        switch (s.Trim().ToLowerInvariant())
        {
            case "auto":
                mode = ProjectMemoryPipelineMode.Auto;
                return true;
            case "ingestonly":
            case "ingest_only":
                mode = ProjectMemoryPipelineMode.IngestOnly;
                return true;
            case "queryonly":
            case "query_only":
                mode = ProjectMemoryPipelineMode.QueryOnly;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// SSE: stream local LLM tokens for a project-memory agent spec; persists user/assistant turns to the shared chat session store (same as CodeGraph).
    /// </summary>
    [HttpPost("playground/message/stream")]
    [Produces("text/event-stream")]
    public async Task PlaygroundMessageStream(
        [FromBody] ProjectMemoryPlaygroundStreamRequestDto body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.SessionId))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (string.IsNullOrWhiteSpace(body.AgentId) || string.IsNullOrWhiteSpace(body.Payload))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var root = RootOrNull();
        if (root == null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("{\"error\":\"Project root not set\"}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var (ctx, loadErr) = await TryLoadContextAsync(root, cancellationToken).ConfigureAwait(false);
        if (loadErr != null || ctx == null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var spec = ctx.AgentSpecs.FirstOrDefault(a => string.Equals(a.Id, body.AgentId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (spec == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var sessionId = body.SessionId.Trim();
        await EnsurePlaygroundSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SessionTurn> prior;
        try
        {
            prior = await _sessions.GetTurnsAsync(sessionId, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playground: could not load turns");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        // Match scenario-scoped flows: body may omit scenarioId — inherit from the session's chat project (same bucket as flow run).
        var scenarioFromBody = body.ScenarioId?.Trim();
        string? scenarioResolved = string.IsNullOrWhiteSpace(scenarioFromBody) ? null : scenarioFromBody;
        if (string.IsNullOrWhiteSpace(scenarioResolved))
        {
            var sessInfo = await _sessions.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sessInfo?.ProjectId))
            {
                var chatProj = await _sessions.GetProjectAsync(sessInfo.ProjectId!, cancellationToken).ConfigureAwait(false);
                if (chatProj is { ScenarioId: var sid } && !string.IsNullOrWhiteSpace(sid))
                    scenarioResolved = sid.Trim();
            }
        }

        var scenarioDef = string.IsNullOrWhiteSpace(scenarioResolved) ? null : _scenarioCatalog.Get(scenarioResolved);
        var flowCatalogOk = scenarioDef?.Flow != null && ScenarioFlowValidator.Validate(scenarioDef).Count == 0;
        var useScenarioFlow = scenarioDef?.Flow != null && flowCatalogOk && !string.IsNullOrWhiteSpace(scenarioResolved);
        var prompt = useScenarioFlow
            ? ""
            : ProjectMemoryPersonaLlmRunner.BuildPlaygroundPrompt(spec, prior, body.Payload, scenarioResolved);
        var turnGroupId = Guid.NewGuid().ToString();
        var messageId = Guid.NewGuid().ToString();

        try
        {
            await AppendPlaygroundTurnAsync(sessionId, SessionRole.User, body.Payload, agentId: null, turnGroupId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playground: append user turn failed");
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Append("X-Accel-Buffering", "no");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, HttpContext.RequestAborted);
        var ct = linked.Token;
        var rootFull = Path.GetFullPath(root);
        var ingestActive = string.Equals(spec.Id, "person-extractor", StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrWhiteSpace(scenarioResolved);

        // One trace per streamed playground turn so the Trace timeline chart can load /api/Visualization/trace/.../timeline.
        using var streamActivity = _activityTracker?.StartActivity("http.project-memory.playground-stream");
        string? streamTraceId = null;
        if (_activityTracker != null)
        {
            var cx = _activityTracker.ExtractContext();
            if (cx.TryGetValue("trace-id", out var tid) && !string.IsNullOrWhiteSpace(tid))
                streamTraceId = tid;
        }

        void SetStreamRootDetail(
            string status,
            string? errorMessage = null,
            IEnumerable<string>? personaChain = null,
            int? responseChars = null,
            bool? ingestAttempted = null)
        {
            try
            {
                streamActivity?.SetTimelineDetailJson(
                    PlaygroundTraceTimelineDetail.BuildStreamRootJson(
                        sessionId,
                        messageId,
                        scenarioResolved,
                        spec.Id,
                        useScenarioFlow,
                        status,
                        errorMessage,
                        personaChain,
                        responseChars,
                        ingestAttempted));
            }
            catch (Exception exDetail)
            {
                _logger.LogDebug(exDetail, "Playground: stream root trace detail JSON skipped");
            }
        }
        SetStreamRootDetail(status: "running");

        async Task WriteSseAsync(AgentStreamEvent evt)
        {
            if (!string.IsNullOrWhiteSpace(streamTraceId))
                evt.TraceId = streamTraceId;
            var json = JsonSerializer.Serialize(evt, JsonSse);
            await Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }

        async Task WriteFlowStepAsync(string stepId, string status, string? detail = null)
        {
            var payload = JsonSerializer.Serialize(new { id = stepId, status, detail }, JsonSse);
            await WriteSseAsync(new AgentStreamEvent { Type = "flow_step", Payload = payload, AgentId = spec.Id }).ConfigureAwait(false);
        }

        // Brief "running" beat so the dashboard can show yellow → final color per chip (skipped stays grey).
        const int flowStepVisualMs = 90;
        async Task PulseFlowStepAsync(string stepId, string? runningDetail, string finalStatus, string? finalDetail = null)
        {
            await WriteFlowStepAsync(stepId, "running", runningDetail).ConfigureAwait(false);
            try
            {
                await Task.Delay(flowStepVisualMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            await WriteFlowStepAsync(stepId, finalStatus, finalDetail).ConfigureAwait(false);
        }

        if (useScenarioFlow)
        {
            var personasSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ingestChipActive = !string.IsNullOrWhiteSpace(scenarioResolved);
            var prefixSteps = PlaygroundFlowPlanBuilder.BuildFlowExecutionPlanPrefix(scenarioDef!.Flow!, ingestChipActive);
            var prefixPayload = new
            {
                steps = prefixSteps
                    .Select(s => new { id = s.Id, label = s.Label, optional = s.Optional, active = s.Active })
                    .ToArray()
            };
            await WriteSseAsync(new AgentStreamEvent
                {
                    Type = "flow_plan",
                    Payload = JsonSerializer.Serialize(prefixPayload, JsonSse),
                    AgentId = spec.Id
                })
                .ConfigureAwait(false);

            // PRD-019: pure yes/no must hit the pipeline generic-inbox confirm path. Otherwise "yes" is fed to
            // person-extractor (no memoryIntents JSON), ingest reports empty intents, and memory-curator narrates incorrectly.
            // Classifier mixes heuristic with LLM intent so natural consent (e.g. "yes I wish to save it") still routes here.
            var lastAssistantPriorPrompt = LastAssistantContent(prior);
            var inboxConfirmSignal = await _confirmClassifier
                .ClassifyAsync(body.Payload, lastAssistantPriorPrompt, ct)
                .ConfigureAwait(false);
            if (inboxConfirmSignal != ConfirmationInputDetector.ConfirmationSignal.None)
            {
                await WriteFlowStepAsync("pm-generic-inbox-confirm", "running", "generic inbox confirm/reject…").ConfigureAwait(false);
                var confirmPipelineReq = new ProjectMemoryPipelineRequest
                {
                    ProjectRoot = rootFull,
                    UserMessage = body.Payload.Trim(),
                    CorrelationId = messageId,
                    Mode = ProjectMemoryPipelineMode.IngestOnly,
                    ConversationPrefix = BuildOrchestratorTranscriptPrefix(prior),
                    ScenarioId = scenarioResolved,
                    SessionId = sessionId,
                    TurnId = turnGroupId
                };
                var confirmPipelineResult = await _pipeline.RunAsync(confirmPipelineReq, ct).ConfigureAwait(false);
                var ranConfirmStep = confirmPipelineResult.Steps.Any(s =>
                    string.Equals(s.Name, "confirm", StringComparison.OrdinalIgnoreCase));
                if (ranConfirmStep)
                {
                    var confirmDetail = confirmPipelineResult.Steps
                        .LastOrDefault(st => string.Equals(st.Name, "confirm", StringComparison.OrdinalIgnoreCase))
                        ?.Detail;
                    await WriteFlowStepAsync("pm-generic-inbox-confirm", "done", confirmDetail ?? "confirm").ConfigureAwait(false);
                    var confirmFinalText =
                        ProjectMemoryUiLinkFormatter.WithAbsoluteWorkspaceLinks(confirmPipelineResult.FinalText, Request);
                    await WriteSseAsync(new AgentStreamEvent
                        {
                            Type = "phase",
                            Payload = "Generic inbox confirmation applied.",
                            AgentId = spec.Id
                        })
                        .ConfigureAwait(false);

                    using (var persistScopeConfirm = _activityTracker?.StartActivity("pm.playground.persist-assistant"))
                    {
                        try
                        {
                            await AppendPlaygroundTurnAsync(sessionId, SessionRole.Assistant, confirmFinalText, spec.Id, turnGroupId, ct)
                                .ConfigureAwait(false);
                            try
                            {
                                persistScopeConfirm?.SetTimelineDetailJson(
                                    PlaygroundTraceTimelineDetail.BuildPersistJson(
                                        sessionId, messageId, confirmFinalText.Length, ingest: null, rootFull));
                            }
                            catch (Exception exDetail)
                            {
                                _logger.LogDebug(exDetail, "Playground: persist trace detail JSON skipped");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Playground: append assistant turn failed");
                        }
                    }

                    var donePayloadConfirm = new
                    {
                        type = "done",
                        status = confirmPipelineResult.Success ? "Success" : "Failed",
                        responseData = confirmFinalText,
                        traceId = streamTraceId,
                        errorMessage = confirmPipelineResult.Success ? (string?)null : confirmFinalText,
                        messageId,
                        sessionId
                    };
                    await Response.WriteAsync("data: " + JsonSerializer.Serialize(donePayloadConfirm, JsonSse) + "\n\n", ct)
                        .ConfigureAwait(false);
                    await Response.Body.FlushAsync(ct).ConfigureAwait(false);
                    SetStreamRootDetail(
                        status: confirmPipelineResult.Success ? "success" : "error",
                        errorMessage: confirmPipelineResult.Success ? null : confirmFinalText,
                        personaChain: new[] { spec.Id },
                        responseChars: confirmFinalText.Length,
                        ingestAttempted: true);
                    return;
                }

                await WriteFlowStepAsync(
                        "pm-generic-inbox-confirm",
                        "skipped",
                        "No pending generic-inbox rows matched this scenario (or confirmation window expired).")
                    .ConfigureAwait(false);

                var noPendingText = "No pending out-of-schema fact matched this scenario, or the confirmation window expired. Please re-enter the fact so I can ask for confirmation again.";
                using (var persistScopeConfirmMiss = _activityTracker?.StartActivity("pm.playground.persist-assistant"))
                {
                    try
                    {
                        await AppendPlaygroundTurnAsync(sessionId, SessionRole.Assistant, noPendingText, spec.Id, turnGroupId, ct)
                            .ConfigureAwait(false);
                        try
                        {
                            persistScopeConfirmMiss?.SetTimelineDetailJson(
                                PlaygroundTraceTimelineDetail.BuildPersistJson(
                                    sessionId, messageId, noPendingText.Length, ingest: null, rootFull));
                        }
                        catch (Exception exDetail)
                        {
                            _logger.LogDebug(exDetail, "Playground: persist trace detail JSON skipped");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Playground: append assistant turn failed");
                    }
                }

                var donePayloadNoPending = new
                {
                    type = "done",
                    status = "Success",
                    responseData = noPendingText,
                    traceId = streamTraceId,
                    errorMessage = (string?)null,
                    messageId,
                    sessionId
                };
                await Response.WriteAsync("data: " + JsonSerializer.Serialize(donePayloadNoPending, JsonSse) + "\n\n", ct)
                    .ConfigureAwait(false);
                await Response.Body.FlushAsync(ct).ConfigureAwait(false);
                SetStreamRootDetail(
                    status: "success",
                    personaChain: new[] { spec.Id },
                    responseChars: noPendingText.Length,
                    ingestAttempted: false);
                return;
            }

            var sseGate = new SemaphoreSlim(1, 1);
            async Task GatedWriteSseAsync(AgentStreamEvent evt)
            {
                await sseGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await WriteSseAsync(evt).ConfigureAwait(false);
                }
                finally
                {
                    sseGate.Release();
                }
            }

            async Task GatedWriteFlowStepAsync(string stepId, string status, string? detail = null)
            {
                await sseGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await WriteFlowStepAsync(stepId, status, detail).ConfigureAwait(false);
                }
                finally
                {
                    sseGate.Release();
                }
            }

            var lastPersona = spec.Id;
            var ollamaBaseFlow = LLMAgent.GetConfiguredOllamaApiUrl().TrimEnd('/') + "/";
            var modelFlow = LLMAgent.GetConfiguredDefaultModel();
            var flowObserver = new PlaygroundScenarioFlowSseObserver(
                scenarioDef.Flow!,
                ingestChipActive,
                GatedWriteSseAsync,
                spec.Id,
                JsonSse);

            // PRD-019 Option B + F: resolve pronouns once before the flow graph executes so the rewritten
            // text reaches every persona via ChatInput → person-extractor. Without this, the playground
            // bypasses ProjectMemoryPipelineRunner and follow-ups like "He likes basketball" extract under
            // the wrong entity (e.g. person_1 instead of raha).
            CoreferencePreprocessResult? corefForFlow = null;
            try
            {
                corefForFlow = await _corefCoordinator
                    .PreprocessAsync(
                        rootFull,
                        scenarioResolved,
                        body.Payload,
                        BuildOrchestratorTranscriptPrefix(prior),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception exCoref)
            {
                _logger.LogDebug(exCoref, "Playground: coref preprocessing skipped");
            }
            var flowUserMessage = corefForFlow?.ResolvedUserMessage ?? body.Payload;

            // memory-curator must see whether ingest actually wrote files (tools are not executed in playground).
            ProjectMemoryIngestResult? lastExtractorIngest = null;

            ScenarioFlowGraphInterpreter.PersonaInvoker invokeFlow = async (personaId, promptText, cancellationToken, flowNodeId) =>
            {
                lastPersona = personaId.Trim();
                personasSeen.Add(lastPersona);
                var pSpec = ctx.AgentSpecs.FirstOrDefault(a =>
                    string.Equals(a.Id, personaId.Trim(), StringComparison.OrdinalIgnoreCase));
                if (pSpec == null)
                    throw new ScenarioFlowExecutionException($"Playground flow: agent spec '{personaId}' was not found.");

                // Defense-in-depth for PRD-019 confirmations in scenario flow. If a short "yes/no" reaches
                // the curator after person-extractor emitted empty JSON, do not let the LLM summarize that
                // empty ingest. Confirm/reject the pending generic-inbox rows with the original user message.
                ConfirmationInputDetector.ConfirmationSignal curatorConfirmSignal =
                    ConfirmationInputDetector.ConfirmationSignal.None;
                if (string.Equals(personaId.Trim(), "memory-curator", StringComparison.OrdinalIgnoreCase))
                {
                    curatorConfirmSignal = await _confirmClassifier
                        .ClassifyAsync(body.Payload, LastAssistantContent(prior), cancellationToken)
                        .ConfigureAwait(false);
                }
                if (curatorConfirmSignal != ConfirmationInputDetector.ConfirmationSignal.None)
                {
                    await GatedWriteFlowStepAsync("pm-generic-inbox-confirm", "running", "generic inbox confirm/reject…")
                        .ConfigureAwait(false);
                    var confirmPipelineReq = new ProjectMemoryPipelineRequest
                    {
                        ProjectRoot = rootFull,
                        UserMessage = body.Payload.Trim(),
                        CorrelationId = messageId,
                        Mode = ProjectMemoryPipelineMode.IngestOnly,
                        ConversationPrefix = BuildOrchestratorTranscriptPrefix(prior),
                        ScenarioId = scenarioResolved,
                        SessionId = sessionId,
                        TurnId = turnGroupId
                    };
                    var confirmPipelineResult = await _pipeline.RunAsync(confirmPipelineReq, cancellationToken).ConfigureAwait(false);
                    var confirmStep = confirmPipelineResult.Steps
                        .LastOrDefault(st => string.Equals(st.Name, "confirm", StringComparison.OrdinalIgnoreCase));
                    if (confirmStep != null)
                    {
                        await GatedWriteFlowStepAsync("pm-generic-inbox-confirm", "done", confirmStep.Detail ?? "confirm")
                            .ConfigureAwait(false);
                        return ProjectMemoryUiLinkFormatter.WithAbsoluteWorkspaceLinks(confirmPipelineResult.FinalText, Request);
                    }

                    await GatedWriteFlowStepAsync(
                            "pm-generic-inbox-confirm",
                            "skipped",
                            "No pending generic-inbox rows matched this scenario (or confirmation window expired).")
                        .ConfigureAwait(false);
                    return "No pending out-of-schema fact matched this scenario, or the confirmation window expired. Please re-enter the fact so I can ask for confirmation again.";
                }

                string? flowAppendix = null;
                if (string.Equals(personaId.Trim(), "memory-curator", StringComparison.OrdinalIgnoreCase))
                {
                    flowAppendix = lastExtractorIngest != null
                        ? ProjectMemoryPersonaLlmRunner.BuildPlaygroundFlowIngestHint(lastExtractorIngest)
                        : """
                          ---
                          Playground runtime: no person-extractor ingest ran before this step (or ingest was skipped).
                          write_document is not executed here. Do not claim markdown files were written to disk.
                          """;
                }

                // Flow mode is strict node-to-node chaining: each persona gets upstream payload as latest input.
                // Prior transcript is intentionally omitted to avoid leaking older turns into intermediate steps.
                var built = ProjectMemoryPersonaLlmRunner.BuildPlaygroundPrompt(
                    pSpec,
                    priorTurns: null,
                    newUserText: promptText,
                    scenarioId: scenarioResolved,
                    playgroundFlowAppendix: flowAppendix);
                await GatedWriteSseAsync(new AgentStreamEvent
                    {
                        Type = "phase",
                        Payload = $"Running {personaId}…",
                        AgentId = spec.Id
                    })
                    .ConfigureAwait(false);

                var accFlow = new StringBuilder();
                using (var personaScopeFlow = _activityTracker?.StartActivity("pm.playground.persona-llm"))
                {
                    using var reqFlow = new HttpRequestMessage(HttpMethod.Post, ollamaBaseFlow + "api/generate");
                    reqFlow.Content = JsonContent.Create(new { model = modelFlow, prompt = built, stream = true });
                    using var respFlow = await LlmHttp.SendAsync(reqFlow, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                    respFlow.EnsureSuccessStatusCode();
                    await using var streamBody = await respFlow.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var readerFlow = new StreamReader(streamBody);
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var line = await readerFlow.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (line == null)
                            break;
                        if (!OllamaStreamLineParser.TryParseLine(line, out var token, out var done))
                            continue;
                        if (!string.IsNullOrEmpty(token))
                        {
                            accFlow.Append(token);
                            await GatedWriteSseAsync(new AgentStreamEvent
                                {
                                    Type = "llm_delta",
                                    Payload = token,
                                    AgentId = spec.Id
                                })
                                .ConfigureAwait(false);
                        }

                        if (done)
                            break;
                    }

                    try
                    {
                        personaScopeFlow?.SetTimelineDetailJson(
                            PlaygroundTraceTimelineDetail.BuildPersonaLlmJson(built, accFlow.ToString(), modelFlow, ollamaBaseFlow));
                    }
                    catch (Exception exDetail)
                    {
                        _logger.LogDebug(exDetail, "Playground: persona trace detail JSON skipped");
                    }
                }

                var rawFlow = accFlow.ToString();
                if (string.Equals(personaId, "person-extractor", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(scenarioResolved))
                {
                    var ingestStepId = !string.IsNullOrWhiteSpace(flowNodeId)
                        ? PlaygroundFlowPlanBuilder.SyntheticIngestIdForExtractorNode(flowNodeId!)
                        : PlaygroundFlowPlanBuilder.IngestStepId;
                    await GatedWriteFlowStepAsync(ingestStepId, "running", "parse + route + projection…").ConfigureAwait(false);
                    using (var ingestScopeFlow = _activityTracker?.StartActivity("pm.playground.ingest-disk"))
                    {
                        try
                        {
                            var ingestFlow = await _pipeline
                                .IngestFromExtractorOutputAsync(rootFull, scenarioResolved!, rawFlow, cancellationToken)
                                .ConfigureAwait(false);
                            await GatedWriteFlowStepAsync(
                                    ingestStepId,
                                    "done",
                                    ingestFlow.WroteAnyFile
                                        ? $"Wrote {ingestFlow.UpdatedFiles.Count} path(s)"
                                        : (ingestFlow.Summary ?? "no files"))
                                .ConfigureAwait(false);
                            try
                            {
                                ingestScopeFlow?.SetTimelineDetailJson(
                                    PlaygroundTraceTimelineDetail.BuildIngestJson(scenarioResolved!, ingestFlow, rawFlow));
                            }
                            catch (Exception exDetail)
                            {
                                _logger.LogDebug(exDetail, "Playground: ingest trace detail JSON skipped");
                            }

                            lastExtractorIngest = ingestFlow;
                            if (!ingestFlow.ParseSuccess)
                                _logger.LogWarning(
                                    "Playground flow ingest parse failed (scenario {Scenario}): {Summary}. Output prefix: {Prefix}",
                                    scenarioResolved,
                                    ingestFlow.Summary ?? "",
                                    ProjectMemoryPersonaLlmRunner.TruncateForIngestLog(rawFlow));

                            // PRD-019 Option B + F: persist active subject after the playground extractor turn
                            // so a brand-new browser session in the same scenario can still resolve "He/She"
                            // back to the right entity. Best-effort: never break the playground SSE flow here.
                            try
                            {
                                await _corefCoordinator
                                    .PersistFocusFromExtractAsync(
                                        rootFull,
                                        scenarioResolved,
                                        rawFlow,
                                        corefForFlow?.ActiveSubjectKey,
                                        corefForFlow?.KnownEntities ?? System.Array.Empty<KnownEntity>(),
                                        sessionId,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception exFocus)
                            {
                                _logger.LogDebug(exFocus, "Playground: persist coreference focus skipped");
                            }

                            // Keep chain purity: downstream LlmNode receives extractor raw output,
                            // while ingest remains a side-effect tracked by flow_step + trace.
                            return rawFlow;
                        }
                        catch (Exception exIngest)
                        {
                            await GatedWriteFlowStepAsync(ingestStepId, "error", exIngest.Message).ConfigureAwait(false);
                            throw;
                        }
                    }
                }

                return rawFlow;
            };

            string fullTextFlow;
            try
            {
                var interpreter = new ScenarioFlowGraphInterpreter();
                fullTextFlow = await interpreter
                    .ExecuteAsync(
                        scenarioDef.Flow!,
                        flowUserMessage,
                        invokeFlow,
                        Timeout.InfiniteTimeSpan,
                        rootFull,
                        _scenarioFlowRouterLlm,
                        flowObserver,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (ScenarioFlowExecutionException ex)
            {
                _logger.LogWarning(ex, "Playground scenario flow failed");
                await WriteSseAsync(new AgentStreamEvent { Type = "error", Payload = ex.Message, AgentId = spec.Id })
                    .ConfigureAwait(false);
                fullTextFlow = "Error: " + ex.Message;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Playground scenario flow failed");
                await WriteSseAsync(new AgentStreamEvent { Type = "error", Payload = ex.Message, AgentId = spec.Id })
                    .ConfigureAwait(false);
                fullTextFlow = "Error: " + ex.Message;
            }

            using (var persistScopeFlow = _activityTracker?.StartActivity("pm.playground.persist-assistant"))
            {
                try
                {
                    await AppendPlaygroundTurnAsync(sessionId, SessionRole.Assistant, fullTextFlow, lastPersona, turnGroupId, ct)
                        .ConfigureAwait(false);
                    try
                    {
                        persistScopeFlow?.SetTimelineDetailJson(
                            PlaygroundTraceTimelineDetail.BuildPersistJson(
                                sessionId, messageId, fullTextFlow.Length, ingest: null, rootFull));
                    }
                    catch (Exception exDetail)
                    {
                        _logger.LogDebug(exDetail, "Playground: persist trace detail JSON skipped");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Playground: append assistant turn failed");
                }
            }

            var donePayloadFlow = new
            {
                type = "done",
                status = "Success",
                responseData = fullTextFlow,
                traceId = streamTraceId,
                errorMessage = (string?)null,
                messageId,
                sessionId
            };
            await Response.WriteAsync("data: " + JsonSerializer.Serialize(donePayloadFlow, JsonSse) + "\n\n", ct)
                .ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            var flowError = fullTextFlow.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
            SetStreamRootDetail(
                status: flowError ? "error" : "success",
                errorMessage: flowError ? fullTextFlow : null,
                personaChain: personasSeen,
                responseChars: fullTextFlow.Length,
                ingestAttempted: lastExtractorIngest != null);
            return;
        }

        var flowPlan = PlaygroundFlowPlanBuilder.Build(scenarioDef, spec.Id, ingestActive, allowSyntheticLlmNode: true);
        var planPayload = new
        {
            steps = flowPlan.Steps
                .Select(s => new { id = s.Id, label = s.Label, optional = s.Optional, active = s.Active })
                .ToArray()
        };
        await WriteSseAsync(new AgentStreamEvent
        {
            Type = "flow_plan",
            Payload = JsonSerializer.Serialize(planPayload, JsonSse),
            AgentId = spec.Id
        }).ConfigureAwait(false);

        var runIdx = PlaygroundFlowPlanBuilder.ResolveRunnerStepIndex(flowPlan.Steps, spec.Id);
        if (runIdx < 0)
        {
            _logger.LogWarning("Playground: no LlmNode step in flow plan for agent {AgentId}", spec.Id);
            for (var j = 0; j < flowPlan.Steps.Count; j++)
            {
                if (!string.Equals(flowPlan.Steps[j].NodeKind, "LlmNode", StringComparison.OrdinalIgnoreCase))
                    continue;
                runIdx = j;
                break;
            }
        }

        if (runIdx < 0 || runIdx >= flowPlan.Steps.Count)
        {
            _logger.LogError("Playground: invalid flow plan (no LlmNode) for agent {AgentId}", spec.Id);
            SetStreamRootDetail(
                status: "error",
                errorMessage: "Invalid playground flow plan (no LlmNode step).");
            await WriteSseAsync(new AgentStreamEvent
            {
                Type = "error",
                Payload = "Invalid playground flow plan (no LlmNode step).",
                AgentId = spec.Id
            }).ConfigureAwait(false);
            return;
        }

        async Task EmitPreStepAsync(PlaygroundFlowPlanBuilder.Step st)
        {
            switch (st.NodeKind)
            {
                case "ChatInput":
                    await PulseFlowStepAsync(
                            st.Id,
                            "Accepting user message…",
                            "done",
                            $"{body.Payload.Length} char(s) → session")
                        .ConfigureAwait(false);
                    return;
                case "Router":
                    await PulseFlowStepAsync(
                            st.Id,
                            "Router…",
                            "done",
                            "Single HTTP request — graph router edges are not evaluated here.")
                        .ConfigureAwait(false);
                    return;
                case "LlmNode":
                    await PulseFlowStepAsync(
                            st.Id,
                            $"{st.Label}…",
                            "done",
                            $"Not invoked — this stream runs only {spec.Id}.")
                        .ConfigureAwait(false);
                    return;
                case "Merge":
                    await PulseFlowStepAsync(
                            st.Id,
                            "Merge…",
                            "done",
                            "Playground does not execute Merge nodes (use Scenarios → flow run for convergence).")
                        .ConfigureAwait(false);
                    return;
                case "Ingest":
                    if (!ingestActive)
                    {
                        var why = !string.Equals(spec.Id, "person-extractor", StringComparison.OrdinalIgnoreCase)
                            ? $"Agent is '{spec.Id}' — only person-extractor emits memoryIntents JSON for disk ingest."
                            : "No scenario id resolved (pick Scenario, or put this session in a chat project with a scenario).";
                        await PulseFlowStepAsync(st.Id, "Apply extractor JSON → disk…", "skipped", why).ConfigureAwait(false);
                    }
                    else
                    {
                        await PulseFlowStepAsync(
                                st.Id,
                                "Apply extractor JSON → disk…",
                                "skipped",
                                "This ingest chip appears before the streamed persona in the graph — disk ingest only runs when Ingest is after the active LlmNode.")
                            .ConfigureAwait(false);
                    }

                    return;
                default:
                    await PulseFlowStepAsync(st.Id, st.Label + "…", "skipped", "Not executed in Playground.").ConfigureAwait(false);
                    return;
            }
        }

        for (var i = 0; i < runIdx; i++)
            await EmitPreStepAsync(flowPlan.Steps[i]).ConfigureAwait(false);

        var ollamaBase = LLMAgent.GetConfiguredOllamaApiUrl().TrimEnd('/') + "/";
        var model = LLMAgent.GetConfiguredDefaultModel();
        var personaDetail =
            $"{spec.Id} @ {rootFull}; {prior.Count} prior turn(s); prompt {prompt.Length} chars"
            + (string.IsNullOrWhiteSpace(scenarioResolved) ? "" : $"; scenarioId={scenarioResolved}")
            + (flowPlan.UsedSyntheticLlmNode
                ? "; selected agent is not a LlmNode on this scenario's sequential path — LLM still uses this YAML."
                : "");
        var runnerStep = flowPlan.Steps[runIdx];
        await WriteFlowStepAsync(runnerStep.Id, "running", personaDetail).ConfigureAwait(false);

        await WriteSseAsync(new AgentStreamEvent
        {
            Type = "phase",
            Payload = $"Running {spec.Id}…",
            AgentId = spec.Id
        }).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        var acc = new StringBuilder();
        var personaFailed = false;
        using (var personaScope = _activityTracker?.StartActivity("pm.playground.persona-llm"))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, ollamaBase + "api/generate");
                req.Content = JsonContent.Create(new { model, prompt, stream = true });

                using var resp = await LlmHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line == null)
                        break;
                    if (!OllamaStreamLineParser.TryParseLine(line, out var token, out var done))
                        continue;
                    if (!string.IsNullOrEmpty(token))
                    {
                        acc.Append(token);
                        await WriteSseAsync(new AgentStreamEvent { Type = "llm_delta", Payload = token, AgentId = spec.Id })
                            .ConfigureAwait(false);
                    }

                    if (done)
                        break;
                }
            }
            catch (Exception ex)
            {
                personaFailed = true;
                _logger.LogWarning(ex, "Playground stream failed");
                await WriteFlowStepAsync(runnerStep.Id, "error", ex.Message).ConfigureAwait(false);
                await WriteSseAsync(new AgentStreamEvent { Type = "error", Payload = ex.Message, AgentId = spec.Id }).ConfigureAwait(false);
                if (acc.Length == 0)
                    acc.Append("Error: ").Append(ex.Message);
            }
            finally
            {
                sw.Stop();
            }

            try
            {
                personaScope?.SetTimelineDetailJson(
                    PlaygroundTraceTimelineDetail.BuildPersonaLlmJson(prompt, acc.ToString(), model, ollamaBase));
            }
            catch (Exception exDetail)
            {
                _logger.LogDebug(exDetail, "Playground: persona trace detail JSON skipped");
            }
        }

        var rawLlm = acc.ToString();
        if (!personaFailed)
            await WriteFlowStepAsync(runnerStep.Id, "done", $"Ollama {rawLlm.Length} char(s)").ConfigureAwait(false);

        var fullText = rawLlm;
        ProjectMemoryIngestResult? ingestSnapshot = null;

        for (var i = runIdx + 1; i < flowPlan.Steps.Count; i++)
        {
            var st = flowPlan.Steps[i];
            switch (st.NodeKind)
            {
                case "LlmNode":
                    await PulseFlowStepAsync(
                            st.Id,
                            $"{st.Label}…",
                            "done",
                            $"Not invoked — only {spec.Id} ran this request.")
                        .ConfigureAwait(false);
                    break;
                case "Merge":
                    await PulseFlowStepAsync(
                            st.Id,
                            "Merge…",
                            "done",
                            "Playground does not execute Merge nodes (use Scenarios → flow run for convergence).")
                        .ConfigureAwait(false);
                    break;
                case "Ingest":
                    if (ingestActive)
                    {
                        await WriteFlowStepAsync(st.Id, "running", "parse + route + projection…").ConfigureAwait(false);
                        using (var ingestScope = _activityTracker?.StartActivity("pm.playground.ingest-disk"))
                        {
                            try
                            {
                                var ingest = await _pipeline
                                    .IngestFromExtractorOutputAsync(rootFull, scenarioResolved!, rawLlm, ct)
                                    .ConfigureAwait(false);
                                ingestSnapshot = ingest;
                                fullText = ProjectMemoryPersonaLlmRunner.AppendIngestFooter(rawLlm, ingest);
                                if (fullText.Length > rawLlm.Length)
                                {
                                    var tail = fullText[rawLlm.Length..];
                                    await WriteSseAsync(new AgentStreamEvent
                                        {
                                            Type = "assistant_tail",
                                            Payload = tail,
                                            AgentId = spec.Id
                                        })
                                        .ConfigureAwait(false);
                                }

                                await WriteFlowStepAsync(st.Id, "done",
                                        ingest.WroteAnyFile
                                            ? $"Wrote {ingest.UpdatedFiles.Count} path(s)"
                                            : (ingest.Summary ?? "no files"))
                                    .ConfigureAwait(false);
                                if (ingest.WroteAnyFile)
                                    _logger.LogInformation("Playground stream: ingested {Count} file(s) for scenario {ScenarioId}",
                                        ingest.UpdatedFiles.Count, scenarioResolved);
                                else if (!ingest.ParseSuccess)
                                    _logger.LogWarning(
                                        "Playground stream ingest parse failed (scenario {Scenario}): {Summary}. Output prefix: {Prefix}",
                                        scenarioResolved,
                                        ingest.Summary ?? "",
                                        ProjectMemoryPersonaLlmRunner.TruncateForIngestLog(rawLlm));
                                try
                                {
                                    ingestScope?.SetTimelineDetailJson(
                                        PlaygroundTraceTimelineDetail.BuildIngestJson(scenarioResolved!, ingest, rawLlm));
                                }
                                catch (Exception exDetail)
                                {
                                    _logger.LogDebug(exDetail, "Playground: ingest trace detail JSON skipped");
                                }
                            }
                            catch (Exception exIngest)
                            {
                                _logger.LogWarning(exIngest, "Playground stream ingest failed");
                                await WriteFlowStepAsync(st.Id, "error", exIngest.Message).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        var why = !string.Equals(spec.Id, "person-extractor", StringComparison.OrdinalIgnoreCase)
                            ? $"Agent is '{spec.Id}' — only person-extractor emits memoryIntents JSON for disk ingest."
                            : "No scenario id resolved (pick Scenario, or put this session in a chat project with a scenario).";
                        await PulseFlowStepAsync(st.Id, "Apply extractor JSON → disk…", "skipped", why).ConfigureAwait(false);
                    }

                    break;
                case "Output":
                    await WriteFlowStepAsync(st.Id, "running", "session store…").ConfigureAwait(false);
                    using (var persistScope = _activityTracker?.StartActivity("pm.playground.persist-assistant"))
                    {
                        try
                        {
                            await AppendPlaygroundTurnAsync(sessionId, SessionRole.Assistant, fullText, agentId: spec.Id, turnGroupId, ct)
                                .ConfigureAwait(false);
                            await WriteFlowStepAsync(st.Id, "done", messageId).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Playground: append assistant turn failed");
                            await WriteFlowStepAsync(st.Id, "error", ex.Message).ConfigureAwait(false);
                        }
                        finally
                        {
                            try
                            {
                                persistScope?.SetTimelineDetailJson(
                                    PlaygroundTraceTimelineDetail.BuildPersistJson(
                                        sessionId, messageId, fullText.Length, ingestSnapshot, rootFull));
                            }
                            catch (Exception exDetail)
                            {
                                _logger.LogDebug(exDetail, "Playground: persist trace detail JSON skipped");
                            }
                        }
                    }

                    break;
                default:
                    await PulseFlowStepAsync(st.Id, st.Label + "…", "skipped", "Not executed in Playground.").ConfigureAwait(false);
                    break;
            }
        }

        var donePayload = new
        {
            type = "done",
            status = "Success",
            responseData = fullText,
            traceId = streamTraceId,
            errorMessage = (string?)null,
            messageId,
            sessionId
        };
        await Response.WriteAsync("data: " + JsonSerializer.Serialize(donePayload, JsonSse) + "\n\n", ct).ConfigureAwait(false);
        await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        SetStreamRootDetail(
            status: personaFailed ? "error" : "success",
            errorMessage: personaFailed ? rawLlm : null,
            personaChain: new[] { spec.Id },
            responseChars: fullText.Length,
            ingestAttempted: ingestSnapshot != null);
    }

    private async Task EnsurePlaygroundSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var s = await _sessions.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (s == null)
            await _sessions.CreateSessionAsync(sessionId, "PM Playground", projectId: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendPlaygroundTurnAsync(
        string sessionId,
        SessionRole role,
        string content,
        string? agentId,
        string turnGroupId,
        CancellationToken cancellationToken)
    {
        var turn = new SessionTurn
        {
            SessionId = sessionId,
            TurnGroupId = turnGroupId,
            Role = role,
            Content = content,
            AgentId = agentId
        };
        await _sessions.AppendTurnAsync(turn, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<AgentTemplateDto> LoadTemplates()
    {
        var path = Path.Combine(_env.WebRootPath ?? "", "templates", "project-memory", "agent-templates.json");
        if (!System.IO.File.Exists(path))
            return Array.Empty<AgentTemplateDto>();

        var json = System.IO.File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<List<AgentTemplateFileRow>>(json, JsonRead);
        if (file == null)
            return Array.Empty<AgentTemplateDto>();

        return file.Select(r => new AgentTemplateDto
        {
            TemplateId = r.TemplateId,
            Name = r.Name,
            Description = r.Description,
            Spec = r.Spec
        }).ToList();
    }

    [HttpPost("agents/from-template")]
    public async Task<ActionResult> CreateFromTemplate([FromBody] CreateAgentFromTemplateRequestDto body, CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();
        if (string.IsNullOrWhiteSpace(body.TemplateId) || string.IsNullOrWhiteSpace(body.NewId))
            return BadRequest("templateId and newId required.");

        var templates = LoadTemplates();
        var t = templates.FirstOrDefault(x => string.Equals(x.TemplateId, body.TemplateId, StringComparison.OrdinalIgnoreCase));
        if (t == null)
            return NotFound("Unknown template.");

        var spec = CloneSpec(t.Spec);
        spec.Id = body.NewId.Trim();
        if (string.IsNullOrWhiteSpace(spec.Name))
            spec.Name = spec.Id;
        var yaml = ProjectYamlSerializer.Serialize(spec);
        var sub = string.IsNullOrWhiteSpace(body.AgentsSubfolder) ? "people" : body.AgentsSubfolder.Trim().Trim('/');
        var relative = $".agctor/agents/{sub}/{spec.Id}.agent.yaml";
        await _files.WriteAsync(root, relative, yaml, cancellationToken).ConfigureAwait(false);
        return Ok(new { saved = true, relativePath = relative });
    }

    [HttpGet("schema")]
    public async Task<ActionResult<SchemaBundleResponseDto>> GetSchema(CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var (ctx, err) = await TryLoadContextAsync(root, cancellationToken).ConfigureAwait(false);
        if (err != null || ctx == null)
            return err!;
        var paths = ctx.ResolvedSchemaPaths;
        if (paths == null)
            return BadRequest("Schema paths not resolved.");

        var files = new Dictionary<string, SchemaFileDto>(StringComparer.OrdinalIgnoreCase);
        await AddFileAsync(files, "project-type", paths.ProjectTypeYaml, root, cancellationToken).ConfigureAwait(false);
        await AddFileAsync(files, "entity-types", paths.EntityTypesYaml, root, cancellationToken).ConfigureAwait(false);
        await AddFileAsync(files, "document-types", paths.DocumentTypesYaml, root, cancellationToken).ConfigureAwait(false);
        await AddFileAsync(files, "routing-rules", paths.RoutingRulesYaml, root, cancellationToken).ConfigureAwait(false);
        await AddFileAsync(files, "workspace", paths.WorkspaceSchemaYaml, root, cancellationToken).ConfigureAwait(false);

        return Ok(new SchemaBundleResponseDto { Files = files });
    }

    private async Task AddFileAsync(Dictionary<string, SchemaFileDto> files, string segment, string fullPath, string root, CancellationToken ct)
    {
        var yaml = await System.IO.File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
        var rel = ProjectMemoryPathSecurity.ToRelativePath(root, fullPath);
        files[segment] = new SchemaFileDto { Segment = segment, RelativePath = rel, Yaml = yaml };
    }

    [HttpPut("schema/{segment}")]
    public async Task<ActionResult> SaveSchemaSegment(string segment, [FromBody] SaveSchemaSegmentRequestDto body, CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var (ctx, err) = await TryLoadContextAsync(root, cancellationToken).ConfigureAwait(false);
        if (err != null || ctx == null)
            return err!;
        var paths = ctx.ResolvedSchemaPaths;
        if (paths == null)
            return BadRequest();

        var full = segment.ToLowerInvariant() switch
        {
            "project-type" => paths.ProjectTypeYaml,
            "entity-types" => paths.EntityTypesYaml,
            "document-types" => paths.DocumentTypesYaml,
            "routing-rules" => paths.RoutingRulesYaml,
            "workspace" => paths.WorkspaceSchemaYaml,
            _ => (string?)null
        };
        if (full == null)
            return NotFound("Unknown segment.");

        var rel = ProjectMemoryPathSecurity.ToRelativePath(root, full);
        await _files.WriteAsync(root, rel, body.Yaml ?? "", cancellationToken).ConfigureAwait(false);
        return Ok(new { saved = true });
    }

    [HttpPost("validate")]
    public async Task<ActionResult<ValidateResponseDto>> Validate(CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        try
        {
            var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            var list = await _entities.DiscoverAsync(ctx, cancellationToken).ConfigureAwait(false);
            var issues = ProjectRebuildValidator.Validate(ctx, list);
            return Ok(new ValidateResponseDto
            {
                Success = !issues.Any(i => i.IsError),
                Issues = issues.Select(MapIssue).ToList()
            });
        }
        catch (Exception ex)
        {
            return Ok(new ValidateResponseDto
            {
                Success = false,
                Issues = new List<ValidationIssueDto>
                {
                    new() { Code = "load", Message = ex.Message, IsError = true }
                }
            });
        }
    }

    [HttpPost("rebuild")]
    public async Task<ActionResult<RebuildResponseDto>> Rebuild(CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var report = await _rebuild.RebuildAsync(root, cancellationToken).ConfigureAwait(false);
        return Ok(new RebuildResponseDto
        {
            Success = report.Success,
            LogPath = report.LogPath,
            Issues = report.Issues.Select(MapIssue).ToList()
        });
    }

    [HttpPost("project-root")]
    public async Task<ActionResult> SetProjectRoot([FromBody] SetProjectRootRequestDto body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.ProjectRoot))
            return BadRequest();
        var path = Path.GetFullPath(body.ProjectRoot.Trim());
        if (!Directory.Exists(Path.Combine(path, ".agctor")))
            return BadRequest(new { error = "Folder must contain a .agctor directory." });
        if (!System.IO.File.Exists(Path.Combine(path, ".agctor", "project.yaml")))
            return BadRequest(new { error = "Folder must contain .agctor/project.yaml." });

        await _userProjectRoot.PersistProjectRootAsync(path, cancellationToken).ConfigureAwait(false);
        return Ok(new { saved = true, projectRoot = path, note = "Saved to appsettings.User.json and applied for new requests." });
    }

    [HttpGet("tree")]
    public ActionResult<TreeNodeDto> GetTree([FromQuery] int maxDepth = 6)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var n = 0;
        var node = BuildTree(root, root, "", 0, Math.Clamp(maxDepth, 1, 12), ref n);
        return Ok(node);
    }

    /// <summary>Git working tree paths under the active project root (requires <c>git</c> on PATH and a <c>.git</c> parent).</summary>
    [HttpGet("workspace/git-changes")]
    [ProducesResponseType(typeof(WorkspaceGitChangesDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkspaceGitChangesDto>> GetWorkspaceGitChanges(CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();
        var dto = await ProjectMemoryGitWorkspaceScanner
            .ListChangesUnderProjectRootAsync(root, _logger, cancellationToken)
            .ConfigureAwait(false);
        return Ok(dto);
    }

    [HttpGet("file")]
    public async Task<ActionResult<FilePreviewDto>> GetFile([FromQuery] string path, CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest();

        try
        {
            var rel = path.Replace('\\', '/').TrimStart('/');
            var text = await _files.ReadAsync(root, rel, cancellationToken).ConfigureAwait(false);
            const int maxLen = 512 * 1024;
            var truncated = text.Length > maxLen;
            if (truncated)
                text = text[..maxLen];
            return Ok(new FilePreviewDto { RelativePath = rel, Content = text, Truncated = truncated });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static TreeNodeDto BuildTree(string projectRoot, string currentDir, string relativeDir, int depth, int maxDepth, ref int count)
    {
        // Large enough for sample projects with many rebuild logs while staying bounded for the dashboard.
        const int maxNodes = 2500;
        var name = string.IsNullOrEmpty(relativeDir) ? Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar)) ?? "project" : Path.GetFileName(currentDir);
        var node = new TreeNodeDto
        {
            Name = name,
            RelativePath = string.IsNullOrEmpty(relativeDir) ? "" : relativeDir.Replace('\\', '/'),
            IsDirectory = true
        };
        if (depth >= maxDepth || count >= maxNodes)
            return node;

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(currentDir)
                .Where(d => !ShouldSkipDir(Path.GetFileName(d)));
        }
        catch
        {
            return node;
        }

        foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (count >= maxNodes)
                break;
            var dn = Path.GetFileName(d);
            var rel = string.IsNullOrEmpty(relativeDir) ? dn : relativeDir + "/" + dn;
            count++;
            node.Children.Add(BuildTree(projectRoot, d, rel, depth + 1, maxDepth, ref count));
        }

        if (depth < maxDepth)
        {
            foreach (var f in Directory.EnumerateFiles(currentDir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (count >= maxNodes)
                    break;
                var fn = Path.GetFileName(f);
                if (fn.StartsWith(".", StringComparison.Ordinal) && fn != ".gitkeep")
                    continue;
                var rel = string.IsNullOrEmpty(relativeDir) ? fn : relativeDir + "/" + fn;
                count++;
                node.Children.Add(new TreeNodeDto
                {
                    Name = fn,
                    RelativePath = rel.Replace('\\', '/'),
                    IsDirectory = false
                });
            }
        }

        return node;
    }

    private static bool ShouldSkipDir(string? name) =>
        name is ".git" or "bin" or "obj" or "node_modules" or ".vs";

    private static ValidationIssueDto MapIssue(ValidationIssue i) =>
        new() { Code = i.Code, Message = i.Message, Path = i.Path, IsError = i.IsError };

    private static AgentDefinitionSpec CloneSpec(AgentDefinitionSpec s)
    {
        var c = JsonSerializer.Deserialize<AgentDefinitionSpec>(JsonSerializer.Serialize(s)) ?? new AgentDefinitionSpec();
        c.SourcePath = null;
        return c;
    }

    private sealed class AgentTemplateFileRow
    {
        public string TemplateId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public AgentDefinitionSpec Spec { get; set; } = new();
    }

    private static bool LooksLikeJson(string text, out string? error)
    {
        error = null;
        var t = (text ?? "").Trim();
        if (!(t.StartsWith("{", StringComparison.Ordinal) || t.StartsWith("[", StringComparison.Ordinal)))
            return false;
        try
        {
            JsonDocument.Parse(t);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

}
