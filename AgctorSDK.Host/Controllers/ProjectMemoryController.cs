using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Coref;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Core.Streaming;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory.Indexing;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Validation;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Core.ProjectMemory.Companion;
using AgctorSDK.Core.ProjectMemory.Inbox;
using AgctorSDK.Core.ProjectMemory.LifeSignals;
using AgctorSDK.Core.ProjectMemory.Privacy;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.ProjectMemory.Tools;
using AgctorSDK.Core.ProjectMemory.Yaml;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.ProjectMemory;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Host.Services.Visual;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
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
    private readonly IConversationFocusStore _focusStore;
    private readonly IAgentFactory _agentFactory;
    private readonly IProactiveSignalsService _proactiveSignals;
    private readonly IGenericInboxDecisionService _inboxDecisions;
    private readonly IPrivacyMemoryService _privacy;
    private readonly VisualPlaygroundAttachmentService? _visualAttachments;
    private readonly VisualPlaygroundStreamExtractService? _streamVisualExtract;
    private readonly GenericInboxVisualEnricher? _inboxVisualEnricher;
    private readonly IOllamaVisionChatClient? _visionChat;
    private readonly IBlobStore? _blobStore;
    private readonly VisualAssetCatalogStore? _visualCatalog;
    private readonly VisualStorageOptions _visualOptions;
    private readonly PlaygroundFocusPostHook _focusPostHook;

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
        IConversationFocusStore focusStore,
        IAgentFactory agentFactory,
        IProactiveSignalsService proactiveSignals,
        IGenericInboxDecisionService inboxDecisions,
        IPrivacyMemoryService privacy,
        ILogger<ProjectMemoryController> logger,
        IActivityTracker? activityTracker = null,
        VisualPlaygroundAttachmentService? visualAttachments = null,
        VisualPlaygroundStreamExtractService? streamVisualExtract = null,
        GenericInboxVisualEnricher? inboxVisualEnricher = null,
        IOllamaVisionChatClient? visionChat = null,
        IBlobStore? blobStore = null,
        VisualAssetCatalogStore? visualCatalog = null,
        IOptions<VisualStorageOptions>? visualOptions = null,
        PlaygroundFocusPostHook? focusPostHook = null)
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
        _focusStore = focusStore ?? throw new ArgumentNullException(nameof(focusStore));
        _agentFactory = agentFactory;
        _proactiveSignals = proactiveSignals ?? throw new ArgumentNullException(nameof(proactiveSignals));
        _inboxDecisions = inboxDecisions ?? throw new ArgumentNullException(nameof(inboxDecisions));
        _privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
        _activityTracker = activityTracker;
        _logger = logger;
        _visualAttachments = visualAttachments;
        _streamVisualExtract = streamVisualExtract;
        _inboxVisualEnricher = inboxVisualEnricher;
        _visionChat = visionChat;
        _blobStore = blobStore;
        _visualCatalog = visualCatalog;
        _visualOptions = visualOptions?.Value ?? new VisualStorageOptions();
        _focusPostHook = focusPostHook ?? throw new ArgumentNullException(nameof(focusPostHook));
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
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Project memory data invalid for root {Root}", root);
            return (null, BadRequest(new { error = ex.Message }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project memory load failed for root {Root}", root);
            return (null, StatusCode(500, new { error = "Project memory load failed.", detail = ex.Message }));
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
            ToolsAllow = a.Tools?.Allow ?? new List<string>(),
            ToolsDeny = a.Tools?.Deny ?? new List<string>(),
            MemoryRead = a.MemoryAccess?.Read ?? new List<string>(),
            MemoryWrite = a.MemoryAccess?.Write ?? new List<string>(),
            Guardrails = a.Guardrails ?? new List<string>(),
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
            conversationPrefix = SessionTranscriptFormatter.BuildPrefix(prior);
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

    /// <summary>Read-only birthday / contact nudges for a scenario workspace (playground Reminders panel).</summary>
    [HttpGet("life-signals")]
    [ProducesResponseType(typeof(PersonLifeSignalsResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PersonLifeSignalsResponseDto>> GetLifeSignals(
        [FromQuery] string scenarioId = "person_3",
        [FromQuery] int staleContactDays = 30,
        [FromQuery] int birthdayHorizonDays = 14,
        CancellationToken cancellationToken = default)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var sid = string.IsNullOrWhiteSpace(scenarioId) ? "person_3" : scenarioId.Trim();
        var signals = await _proactiveSignals
            .ScanAsync(root, sid, staleContactDays, birthdayHorizonDays, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new PersonLifeSignalsResponseDto
        {
            ScenarioId = sid,
            Signals = signals.Select(s => new PersonLifeSignalDto
            {
                EntityKey = s.EntityKey,
                DisplayName = s.DisplayName,
                Kind = s.Kind,
                Message = s.Message,
                DaysUntil = s.DaysUntil,
                Priority = s.Priority
            }).ToList()
        });
    }

    /// <summary>One-line note → ingest pipeline (API only; playground uses scenario flow + router instead).</summary>
    [HttpPost("quick-capture")]
    [ProducesResponseType(typeof(ProjectMemoryOrchestratorRunResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectMemoryOrchestratorRunResponseDto>> QuickCaptureAsync(
        [FromBody] ProjectMemoryQuickCaptureRequestDto body,
        CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();
        if (string.IsNullOrWhiteSpace(body.Text))
            return BadRequest(new { error = "text is required." });

        var wrapped = "[Quick capture — store as timeline observation or profile fact when appropriate]\n" + body.Text.Trim();
        return await RunIngestCaptureAsync(
            wrapped,
            body.ScenarioId?.Trim() ?? "person_3",
            body.SessionId?.Trim(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replay session transcript through ingest (API only; playground uses scenario flow + router instead).</summary>
    [HttpPost("sessions/{sessionId}/capture-to-memory")]
    [ProducesResponseType(typeof(ProjectMemoryOrchestratorRunResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectMemoryOrchestratorRunResponseDto>> CaptureSessionToMemoryAsync(
        [FromRoute] string sessionId,
        [FromBody] ProjectMemorySessionCaptureRequestDto? body,
        CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "sessionId is required." });

        var prior = await _sessions.GetTurnsAsync(sessionId.Trim(), null, cancellationToken).ConfigureAwait(false);
        if (prior.Count == 0)
            return BadRequest(new { error = "Session has no turns to capture." });

        var prefix = SessionTranscriptFormatter.BuildPrefix(prior);
        var wrapped = "[Session capture — extract durable facts and timeline observations from this conversation]\n"
                        + (prefix ?? "");
        return await RunIngestCaptureAsync(
            wrapped,
            body?.ScenarioId?.Trim() ?? "person_3",
            sessionId.Trim(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionResult<ProjectMemoryOrchestratorRunResponseDto>> RunIngestCaptureAsync(
        string userMessage,
        string scenarioId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var req = new ProjectMemoryPipelineRequest
        {
            ProjectRoot = RootOrNull()!,
            UserMessage = userMessage,
            CorrelationId = Guid.NewGuid().ToString("N"),
            Mode = ProjectMemoryPipelineMode.IngestOnly,
            ScenarioId = scenarioId,
            SessionId = sessionId
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

    /// <summary>PRD-022a: pending generic-inbox rows for the confirmation panel.</summary>
    [HttpGet("generic-inbox/pending")]
    [ProducesResponseType(typeof(GenericInboxPendingListResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenericInboxPendingListResponseDto>> ListGenericInboxPendingAsync(
        [FromQuery] string scenarioId = "person_3",
        CancellationToken cancellationToken = default)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var sid = string.IsNullOrWhiteSpace(scenarioId) ? "person_3" : scenarioId.Trim();
        var rows = await _inboxDecisions.ListPendingAsync(root, sid, cancellationToken).ConfigureAwait(false);
        var items = rows.Select(r => new GenericInboxPendingItemDto
        {
            ProposalId = r.ProposalId,
            EntityKey = r.EntityKey,
            KnowledgeType = r.KnowledgeType,
            Attribute = r.Attribute,
            Value = r.Value,
            Confidence = r.Confidence,
            Disposition = r.Disposition,
            ScenarioSegment = r.ScenarioSegment,
            QueuedAtUtc = r.QueuedAtUtc,
            UserPromptLine = string.IsNullOrWhiteSpace(r.UserPromptLine)
                ? $"{r.EntityKey}: {r.Value}"
                : r.UserPromptLine,
            SourceAssetId = string.IsNullOrWhiteSpace(r.SourceAssetId) ? null : r.SourceAssetId.Trim()
        }).ToList();

        if (_inboxVisualEnricher != null)
        {
            try
            {
                await _inboxVisualEnricher
                    .EnrichWithSourceAssetsAsync(root, sid, items, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exInboxVis)
            {
                _logger.LogDebug(exInboxVis, "Inbox visual thumbnail enrichment skipped");
            }
        }

        return Ok(new GenericInboxPendingListResponseDto
        {
            ScenarioId = sid,
            Items = items
        });
    }

    /// <summary>PRD-022a: approve or reject pending inbox rows (replay runs on approve).</summary>
    [HttpPost("generic-inbox/decide")]
    [ProducesResponseType(typeof(GenericInboxDecideResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenericInboxDecideResponseDto>> DecideGenericInboxAsync(
        [FromBody] GenericInboxDecideRequestDto body,
        CancellationToken cancellationToken = default)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();
        if (body.Decisions == null || body.Decisions.Count == 0)
            return BadRequest(new { error = "decisions are required." });

        var sid = string.IsNullOrWhiteSpace(body.ScenarioId) ? "person_3" : body.ScenarioId.Trim();
        var decisions = body.Decisions
            .Select(d => new GenericInboxDecision { ProposalId = d.ProposalId ?? "", Approve = d.Approve })
            .ToList();

        var result = await _inboxDecisions
            .ApplyDecisionsAsync(root, sid, decisions, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new GenericInboxDecideResponseDto
        {
            Approved = result.Approved,
            Rejected = result.Rejected,
            RejectedMismatch = result.RejectedMismatch,
            UpdatedFiles = result.UpdatedFiles,
            Errors = result.Errors
        });
    }

    [HttpGet("privacy/settings")]
    [ProducesResponseType(typeof(CompanionPrivacySettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanionPrivacySettingsDto>> GetPrivacySettingsAsync(CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var s = await _privacy.GetSettingsAsync(root, cancellationToken).ConfigureAwait(false);
        return Ok(new CompanionPrivacySettingsDto { AutoIngestOnSessionEnd = s.AutoIngestOnSessionEnd });
    }

    [HttpPut("privacy/settings")]
    [ProducesResponseType(typeof(CompanionPrivacySettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CompanionPrivacySettingsDto>> PutPrivacySettingsAsync(
        [FromBody] CompanionPrivacySettingsDto body,
        CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var saved = await _privacy.UpdateSettingsAsync(
            root,
            new CompanionPrivacySettings { AutoIngestOnSessionEnd = body.AutoIngestOnSessionEnd },
            cancellationToken).ConfigureAwait(false);

        return Ok(new CompanionPrivacySettingsDto { AutoIngestOnSessionEnd = saved.AutoIngestOnSessionEnd });
    }

    [HttpPost("privacy/forget-person")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForgetPersonAsync(
        [FromBody] ForgetPersonRequestDto body,
        CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();
        if (string.IsNullOrWhiteSpace(body.ScenarioId) || string.IsNullOrWhiteSpace(body.EntityKey))
            return BadRequest(new { error = "scenarioId and entityKey are required." });

        var removed = await _privacy
            .ForgetPersonAsync(root, body.ScenarioId.Trim(), body.EntityKey.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (!removed)
            return NotFound(new { error = "Person folder not found." });

        if (body.ClearProjectFocusWhenMatched && !string.IsNullOrWhiteSpace(body.ProjectId))
        {
            var project = await _sessions.GetProjectAsync(body.ProjectId.Trim(), cancellationToken).ConfigureAwait(false);
            if (project != null
                && string.Equals(project.FocusEntityKey, body.EntityKey.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                project.FocusEntityKey = null;
                project.FocusDisplayName = null;
                project.UpdatedAt = DateTimeOffset.UtcNow;
                await _sessions.UpdateProjectAsync(project, cancellationToken).ConfigureAwait(false);
            }
        }

        return Ok(new { ok = true });
    }

    [HttpGet("privacy/export")]
    [Produces("application/zip")]
    public async Task<IActionResult> ExportScenarioPeopleAsync(
        [FromQuery] string scenarioId = "person_3",
        CancellationToken cancellationToken = default)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var sid = string.IsNullOrWhiteSpace(scenarioId) ? "person_3" : scenarioId.Trim();
        try
        {
            var stream = await _privacy.ExportScenarioPeopleZipAsync(root, sid, cancellationToken).ConfigureAwait(false);
            var fileName = $"people-{sid}-{DateTime.UtcNow:yyyyMMdd}.zip";
            return File(stream, "application/zip", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
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

    /// <summary>Lists entity folder slugs under <c>scenarios/&lt;id&gt;/people/</c> for project focus picker.</summary>
    [HttpGet("scenario-entities")]
    [ProducesResponseType(typeof(IReadOnlyList<ScenarioEntityListItemDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ScenarioEntityListItemDto>> ListScenarioEntities([FromQuery] string? scenarioId = null)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var sid = string.IsNullOrWhiteSpace(scenarioId) ? "people" : scenarioId.Trim();
        var workspace = PersonaScenarioScope.GetEntityWorkspaceRoot(root, sid);
        var peopleDir = Path.Combine(workspace, "people");
        if (!Directory.Exists(peopleDir))
            return Ok(Array.Empty<ScenarioEntityListItemDto>());

        var list = new List<ScenarioEntityListItemDto>();
        foreach (var dir in Directory.EnumerateDirectories(peopleDir))
        {
            var key = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(key) || key.StartsWith('.'))
                continue;
            if (FocusEntityPolicy.IsPlaceholderSlug(key))
                continue;

            var display = key;
            var profilePath = Path.Combine(dir, "profile.md");
            if (System.IO.File.Exists(profilePath))
            {
                var text = System.IO.File.ReadAllText(profilePath);
                var m = System.Text.RegularExpressions.Regex.Match(text, @"(?im)^\s*[-*]?\s*name\s*:\s*(.+)$");
                if (m.Success)
                    display = m.Groups[1].Value.Trim();
            }

            list.Add(new ScenarioEntityListItemDto { EntityKey = key, DisplayName = display });
        }

        list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return Ok(list);
    }

    /// <summary>
    /// On session open: infer focus from project name when unset, persist to SQLite + conversation coref store.
    /// </summary>
    [HttpPost("playground/sync-focus")]
    [ProducesResponseType(typeof(PlaygroundSyncFocusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlaygroundSyncFocusResponseDto>> SyncPlaygroundFocusAsync(
        [FromBody] PlaygroundSyncFocusRequestDto? body,
        CancellationToken cancellationToken)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.SessionId))
            return BadRequest(new ErrorResponse { Code = "SESSION_REQUIRED", Message = "sessionId is required." });

        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var session = await _sessions.GetSessionAsync(body.SessionId.Trim(), cancellationToken).ConfigureAwait(false);
        if (session == null)
            return NotFound(new ErrorResponse { Code = "SESSION_NOT_FOUND", Message = "Session not found." });

        var projectId = !string.IsNullOrWhiteSpace(body.ProjectId)
            ? body.ProjectId.Trim()
            : session.ProjectId;
        if (string.IsNullOrWhiteSpace(projectId))
            return BadRequest(new ErrorResponse { Code = "PROJECT_REQUIRED", Message = "Session is not linked to a project." });

        var project = await _sessions.GetProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project == null)
            return NotFound(new ErrorResponse { Code = "PROJECT_NOT_FOUND", Message = "Project not found." });

        var rootFull = Path.GetFullPath(root);
        var entities = ListScenarioEntityTuples(rootFull, project.ScenarioId);
        var inferred = false;
        var fromConversation = false;
        var focusKey = FocusEntityPolicy.NormalizeSlugOrNull(project.FocusEntityKey);
        var focusDisplay = project.FocusDisplayName;

        ConversationFocus? conversationFocus = null;
        try
        {
            conversationFocus = await _focusStore.LoadAsync(rootFull, project.ScenarioId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            /* best-effort */
        }

        var conversationKey = FocusEntityPolicy.NormalizeSlugOrNull(conversationFocus?.EntityKey);
        if (!string.IsNullOrWhiteSpace(conversationKey)
            && !string.Equals(conversationKey, focusKey, StringComparison.OrdinalIgnoreCase))
        {
            focusKey = conversationKey;
            focusDisplay = conversationFocus!.DisplayName
                           ?? entities.FirstOrDefault(e =>
                               string.Equals(e.EntityKey, conversationKey, StringComparison.OrdinalIgnoreCase)).DisplayName
                           ?? conversationKey;
            fromConversation = true;
            project = await UpdateProjectFocusAsync(project, focusKey, focusDisplay, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(focusKey))
        {
            var guess = FocusEntityPolicy.TryInferFromProjectName(project.Name, entities);
            if (guess != null)
            {
                focusKey = guess.Value.EntityKey;
                focusDisplay = guess.Value.DisplayName;
                inferred = true;
                project = await UpdateProjectFocusAsync(project, focusKey, focusDisplay, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!string.IsNullOrWhiteSpace(focusKey))
            await ApplyChatProjectFocusAsync(rootFull, project, body.SessionId.Trim(), cancellationToken).ConfigureAwait(false);

        return Ok(new PlaygroundSyncFocusResponseDto
        {
            FocusEntityKey = focusKey,
            FocusDisplayName = focusDisplay,
            InferredFromProjectName = inferred,
            UpdatedFromConversation = fromConversation
        });
    }

    private async Task<SessionProject> UpdateProjectFocusAsync(
        SessionProject project,
        string focusKey,
        string? focusDisplay,
        CancellationToken cancellationToken)
    {
        return await _sessions.UpdateProjectAsync(new SessionProject
        {
            ProjectId = project.ProjectId,
            Name = project.Name,
            ScenarioId = project.ScenarioId,
            FocusEntityKey = focusKey,
            FocusDisplayName = focusDisplay,
            SettingsJson = project.SettingsJson,
            CreatedAt = project.CreatedAt,
            SessionCount = project.SessionCount
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>After extract/coref, mirror conversation focus into SQLite so the Focus person dropdown stays in sync.</summary>
    private async Task ApplyPlaygroundFocusPostHookAsync(
        string projectRoot,
        string? scenarioId,
        SessionProject? project,
        string sessionId,
        string? entityKey,
        string? displayName,
        string source,
        Func<AgentStreamEvent, Task> writeSseAsync,
        string agentId,
        CancellationToken cancellationToken)
    {
        var payload = await _focusPostHook
            .ApplyAsync(projectRoot, scenarioId, project, sessionId, entityKey, displayName, source, cancellationToken)
            .ConfigureAwait(false);
        if (payload == null)
            return;

        if (project != null && payload.UpdatedProject)
        {
            project.FocusEntityKey = payload.FocusEntityKey;
            project.FocusDisplayName = payload.FocusDisplayName;
        }

        await writeSseAsync(PlaygroundFocusSse.FocusUpdated(payload, agentId)).ConfigureAwait(false);
    }

    private static List<(string EntityKey, string DisplayName)> ListScenarioEntityTuples(string projectRoot, string? scenarioId)
    {
        var sid = string.IsNullOrWhiteSpace(scenarioId) ? "people" : scenarioId.Trim();
        var workspace = PersonaScenarioScope.GetEntityWorkspaceRoot(projectRoot, sid);
        var peopleDir = Path.Combine(workspace, "people");
        var list = new List<(string, string)>();
        if (!Directory.Exists(peopleDir))
            return list;

        foreach (var dir in Directory.EnumerateDirectories(peopleDir))
        {
            var key = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(key) || key.StartsWith('.') || FocusEntityPolicy.IsPlaceholderSlug(key))
                continue;

            var display = key;
            var profilePath = Path.Combine(dir, "profile.md");
            if (System.IO.File.Exists(profilePath))
            {
                var text = System.IO.File.ReadAllText(profilePath);
                var m = System.Text.RegularExpressions.Regex.Match(text, @"(?im)^\s*[-*]?\s*name\s*:\s*(.+)$");
                if (m.Success)
                    display = m.Groups[1].Value.Trim();
            }

            list.Add((key, display));
        }

        return list;
    }

    private static (string? Key, string? Display) ResolvePlaygroundActiveSubject(
        CoreferencePreprocessResult? coref,
        SessionProject? chatProject)
    {
        var key = FocusEntityPolicy.CoalesceActiveSubject(coref?.ActiveSubjectKey, chatProject?.FocusEntityKey);
        if (string.IsNullOrWhiteSpace(key))
            return (null, null);

        var display = coref?.ActiveSubjectDisplay ?? chatProject?.FocusDisplayName;
        if (string.IsNullOrWhiteSpace(display))
            display = key;
        return (key, display);
    }

    private int ResolveProjectVisualMaxPhotos(SessionProject? chatProject) =>
        ChatProjectSettings.ResolveVisualMaxPhotos(
            chatProject?.VisualMaxPhotos,
            _visualOptions.DefaultVisualContextPhotos,
            _visualOptions.MaxVisualContextPhotos);

    private async Task ApplyChatProjectFocusAsync(
        string projectRoot,
        SessionProject? chatProject,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (chatProject == null || string.IsNullOrWhiteSpace(chatProject.FocusEntityKey))
            return;

        var focus = new ConversationFocus
        {
            EntityKey = PersonaScenarioScope.SanitizeFolderSegment(chatProject.FocusEntityKey).ToLowerInvariant(),
            DisplayName = string.IsNullOrWhiteSpace(chatProject.FocusDisplayName)
                ? chatProject.FocusEntityKey
                : chatProject.FocusDisplayName.Trim(),
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
            UpdatedBySessionId = sessionId,
            Source = "project"
        };
        await _focusStore.SaveAsync(projectRoot, chatProject.ScenarioId, focus, cancellationToken).ConfigureAwait(false);
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

        var hasText = !string.IsNullOrWhiteSpace(body.Payload);
        var hasAttachments = body.Attachments != null && body.Attachments.Count > 0;
        if (string.IsNullOrWhiteSpace(body.AgentId) || (!hasText && !hasAttachments))
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
        var sessInfo = await _sessions.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var scenarioFromBody = body.ScenarioId?.Trim();
        string? scenarioResolved = string.IsNullOrWhiteSpace(scenarioFromBody) ? null : scenarioFromBody;
        if (string.IsNullOrWhiteSpace(scenarioResolved) && !string.IsNullOrWhiteSpace(sessInfo?.ProjectId))
        {
            var chatProj = await _sessions.GetProjectAsync(sessInfo.ProjectId!, cancellationToken).ConfigureAwait(false);
            if (chatProj is { ScenarioId: var sid } && !string.IsNullOrWhiteSpace(sid))
                scenarioResolved = sid.Trim();
        }

        SessionProject? chatProject = null;
        if (!string.IsNullOrWhiteSpace(sessInfo?.ProjectId))
            chatProject = await _sessions.GetProjectAsync(sessInfo.ProjectId!, cancellationToken).ConfigureAwait(false);

        var scenarioDef = string.IsNullOrWhiteSpace(scenarioResolved) ? null : _scenarioCatalog.Get(scenarioResolved);
        var flowCatalogOk = scenarioDef?.Flow != null && ScenarioFlowValidator.Validate(scenarioDef).Count == 0;
        var useScenarioFlow = scenarioDef?.Flow != null && flowCatalogOk && !string.IsNullOrWhiteSpace(scenarioResolved);
        var promptText = hasText ? body.Payload.Trim() : "(User attached image(s) without a caption.)";
        var prompt = useScenarioFlow
            ? ""
            : ProjectMemoryPersonaLlmRunner.BuildPlaygroundPrompt(spec, prior, promptText, scenarioResolved);
        var turnGroupId = string.IsNullOrWhiteSpace(body.TurnGroupId)
            ? Guid.NewGuid().ToString()
            : body.TurnGroupId.Trim();
        var messageId = Guid.NewGuid().ToString();

        string? attachmentsJson = null;
        if (hasAttachments)
        {
            var env = new SessionAttachmentEnvelope();
            foreach (var att in body.Attachments!)
            {
                if (string.IsNullOrWhiteSpace(att.AssetId))
                    continue;
                env.Attachments.Add(new SessionAttachmentRef
                {
                    AssetId = att.AssetId.Trim(),
                    State = string.IsNullOrWhiteSpace(att.State) ? "uploaded" : att.State.Trim(),
                    FileName = att.FileName,
                    Mime = att.Mime
                });
            }

            attachmentsJson = SessionAttachmentJson.Serialize(env);
        }

        var userTurnContent = hasText ? body.Payload.Trim() : "";

        try
        {
            await AppendPlaygroundTurnAsync(
                    sessionId,
                    SessionRole.User,
                    userTurnContent,
                    agentId: null,
                    turnGroupId,
                    attachmentsJson,
                    cancellationToken)
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

        var streamVisualAssetIds = hasAttachments && body.Attachments != null
            ? body.Attachments
                .Where(a => !string.IsNullOrWhiteSpace(a.AssetId))
                .Select(a => a.AssetId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        async Task RunStreamVisualExtractIfNeededAsync()
        {
            if (streamVisualAssetIds.Count == 0
                || _streamVisualExtract == null
                || string.IsNullOrWhiteSpace(scenarioResolved))
                return;

            try
            {
                await _streamVisualExtract
                    .RunAsync(
                        rootFull,
                        scenarioResolved,
                        streamVisualAssetIds,
                        promptText,
                        chatProject?.FocusEntityKey,
                        async (type, payload) => await WriteSseAsync(new AgentStreamEvent { Type = type, Payload = payload })
                            .ConfigureAwait(false),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception exExtract)
            {
                _logger.LogDebug(exExtract, "Playground: stream visual extract skipped");
            }
        }

        if (hasAttachments && _visualAttachments != null && !string.IsNullOrWhiteSpace(scenarioResolved))
        {
            try
            {
                var linkedAttachments = await _visualAttachments
                    .LinkAndEnrichAsync(
                        rootFull,
                        scenarioResolved,
                        sessionId,
                        turnGroupId,
                        body.Attachments,
                        userMessage: promptText,
                        focusEntityKey: chatProject?.FocusEntityKey,
                        queueBackgroundExtract: false,
                        ct)
                    .ConfigureAwait(false);
                foreach (var att in linkedAttachments)
                {
                    var tagDetail = att.EntityKeys is { Count: > 0 }
                        ? "Tagged " + string.Join(", ", att.EntityKeys) + " · analyzing photo…"
                        : "Analyzing photo…";
                    await WriteSseAsync(new AgentStreamEvent
                    {
                        Type = "attachment_state",
                        Payload = VisualPlaygroundAttachmentService.SerializeSsePayload(new
                        {
                            assetId = att.AssetId,
                            state = att.State,
                            detail = tagDetail,
                            entityKeys = att.EntityKeys
                        })
                    }).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(att.ViewUrl))
                    {
                        await WriteSseAsync(new AgentStreamEvent
                        {
                            Type = "attachment_preview",
                            Payload = VisualPlaygroundAttachmentService.SerializeSsePayload(new
                            {
                                assetId = att.AssetId,
                                viewUrl = att.ViewUrl,
                                expiresAt = att.ViewUrlExpiresAt
                            })
                        }).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exAttach)
            {
                _logger.LogDebug(exAttach, "Playground: attachment SSE skipped");
            }
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
            var prefixSteps = PlaygroundFlowPlanBuilder.BuildFlowExecutionPlanPrefix(
                scenarioDef!.Flow!,
                ingestChipActive,
                includeVisualExtractStep: hasAttachments);
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
                    ConversationPrefix = SessionTranscriptFormatter.BuildPrefix(prior),
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
                            await AppendPlaygroundTurnAsync(sessionId, SessionRole.Assistant, confirmFinalText, spec.Id, turnGroupId, null, ct)
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
                    await RunStreamVisualExtractIfNeededAsync().ConfigureAwait(false);
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
                        await AppendPlaygroundTurnAsync(sessionId, SessionRole.Assistant, noPendingText, spec.Id, turnGroupId, null, ct)
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
                await RunStreamVisualExtractIfNeededAsync().ConfigureAwait(false);
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
                hasAttachments,
                GatedWriteSseAsync,
                spec.Id,
                JsonSse);

            // Seed conversation focus from the project only when the scenario has no persisted focus yet.
            // Overwriting every turn prevented chat-driven shifts (e.g. talking about Ryan while project is Raha).
            try
            {
                ConversationFocus? existingFocus = null;
                try
                {
                    existingFocus = await _focusStore
                        .LoadAsync(rootFull, scenarioResolved, ct)
                        .ConfigureAwait(false);
                }
                catch
                {
                    /* best-effort */
                }

                if (existingFocus == null || string.IsNullOrWhiteSpace(existingFocus.EntityKey))
                    await ApplyChatProjectFocusAsync(rootFull, chatProject, sessionId, ct).ConfigureAwait(false);
            }
            catch (Exception exFocusBootstrap)
            {
                _logger.LogDebug(exFocusBootstrap, "Playground: chat project focus bootstrap skipped");
            }

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
                        SessionTranscriptFormatter.BuildPrefix(prior),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception exCoref)
            {
                _logger.LogDebug(exCoref, "Playground: coref preprocessing skipped");
            }

            // FocusSubjectTool (via coordinator) picks active subject; post-hook syncs SQLite + UI.
            if (!string.IsNullOrWhiteSpace(corefForFlow?.ActiveSubjectKey))
            {
                try
                {
                    await ApplyPlaygroundFocusPostHookAsync(
                            rootFull,
                            scenarioResolved,
                            chatProject,
                            sessionId,
                            corefForFlow.ActiveSubjectKey,
                            corefForFlow.ActiveSubjectDisplay,
                            "focus-subject",
                            GatedWriteSseAsync,
                            spec.Id,
                            ct)
                        .ConfigureAwait(false);
                }
                catch (Exception exFocusCoref)
                {
                    _logger.LogDebug(exFocusCoref, "Playground: focus-subject post-hook skipped");
                }
            }

            var flowUserMessage = corefForFlow?.ResolvedUserMessage ?? body.Payload;
            var (playgroundActiveKey, playgroundActiveDisplay) = ResolvePlaygroundActiveSubject(corefForFlow, chatProject);
            var projectVisualMaxPhotos = ResolveProjectVisualMaxPhotos(chatProject);

            // memory-curator must see whether ingest actually wrote files (tools are not executed in playground).
            ProjectMemoryIngestResult? lastExtractorIngest = null;
            string? lastExtractorRawOutput = null;

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
                        ConversationPrefix = SessionTranscriptFormatter.BuildPrefix(prior),
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

                // Persona scope wraps context tools + LLM so trace timeline nests tools under the agent run.
                OllamaStreamAccumulation flowStream;
                using (var personaScopeFlow = _activityTracker?.StartActivity("pm.playground.persona-llm"))
                {
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
                else
                {
                    var flowNode = scenarioDef.Flow?.Nodes?.FirstOrDefault(n =>
                        !string.IsNullOrWhiteSpace(flowNodeId)
                        && string.Equals(n.Id, flowNodeId, StringComparison.OrdinalIgnoreCase));
                    var appendixParts = new List<string>();
                    if (PlaygroundPersonQueryContextBuilder.ShouldLoadPersonMemoryContext(pSpec, flowNode?.Config))
                    {
                        var strat = PlaygroundPersonQueryContextBuilder.ParseStrategy(flowNode?.Config);
                        try
                        {
                            var memoryAppendix = await PlaygroundPersonQueryContextBuilder
                                .BuildFlowAppendixAsync(
                                    _loader,
                                    _entities,
                                    _agentFactory,
                                    pSpec,
                                    personaId.Trim(),
                                    rootFull,
                                    scenarioResolved,
                                    strat,
                                    flowUserMessage,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(memoryAppendix))
                                appendixParts.Add(memoryAppendix);
                        }
                        catch (Exception exCtx)
                        {
                            _logger.LogWarning(exCtx, "Playground: person-memory-context load failed for {PersonaId}", personaId);
                            appendixParts.Add("---\nPerson-memory context failed to load: " + exCtx.Message + "\n");
                        }
                    }

                    if (PlaygroundPersonQueryContextBuilder.ShouldLoadPersonVisualContext(pSpec, flowNode?.Config))
                    {
                        try
                        {
                            var visualContext = await PlaygroundPersonQueryContextBuilder
                                .BuildVisualContextAsync(
                                    _agentFactory,
                                    pSpec,
                                    personaId.Trim(),
                                    rootFull,
                                    scenarioResolved,
                                    flowUserMessage,
                                    chatProject?.FocusEntityKey,
                                    projectVisualMaxPhotos,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(visualContext.Appendix))
                                appendixParts.Add(visualContext.Appendix);

                            var visionReady = _visionChat != null && _blobStore != null && _visualCatalog != null;
                            if (VisualSceneSummary.ShouldUsePersonQueryVision(
                                    personaId.Trim(),
                                    flowUserMessage,
                                    visualContext,
                                    visionReady))
                            {
                                var queryAssetIds = VisualSceneSummary.ResolveQueryAssetIds(
                                    hasAttachments,
                                    body.Attachments?
                                        .Where(a => !string.IsNullOrWhiteSpace(a.AssetId))
                                        .Select(a => a.AssetId.Trim()),
                                    visualContext,
                                    maxAssets: 1);
                                var liveScene = await PlaygroundPersonQueryVisionHelper
                                    .DescribePrimaryAssetAsync(
                                        _visionChat!,
                                        _visualCatalog!,
                                        _blobStore!,
                                        rootFull,
                                        scenarioResolved!,
                                        queryAssetIds,
                                        flowUserMessage,
                                        chatProject?.FocusEntityKey,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                                if (VisualSceneSummary.IsUseful(liveScene))
                                {
                                    appendixParts.Add(
                                        "Visual scene (live vision, saved to catalog):\n  scene: " + liveScene);
                                }
                            }
                        }
                        catch (Exception exVisual)
                        {
                            _logger.LogWarning(exVisual, "Playground: person-visual-context load failed for {PersonaId}", personaId);
                            appendixParts.Add("---\nPerson-visual context failed to load: " + exVisual.Message + "\n");
                        }
                    }

                    if (appendixParts.Count > 0)
                        flowAppendix = string.Join("\n\n", appendixParts);
                }

                // Flow mode is strict node-to-node chaining: each persona gets upstream payload as latest input.
                // Prior transcript is intentionally omitted to avoid leaking older turns into intermediate steps.
                var built = ProjectMemoryPersonaLlmRunner.BuildPlaygroundPrompt(
                    pSpec,
                    priorTurns: null,
                    newUserText: promptText,
                    scenarioId: scenarioResolved,
                    playgroundFlowAppendix: flowAppendix,
                    activeSubjectEntityKey: playgroundActiveKey,
                    activeSubjectDisplayName: playgroundActiveDisplay);
                await GatedWriteSseAsync(new AgentStreamEvent
                    {
                        Type = "phase",
                        Payload = $"Running {personaId}…",
                        AgentId = spec.Id
                    })
                    .ConfigureAwait(false);

                    try
                    {
                        var useVision = hasAttachments
                                        && PlaygroundPersonaMultimodalHelper.ShouldUseVision(personaId.Trim(), hasAttachments)
                                        && _visionChat != null
                                        && _blobStore != null
                                        && _visualCatalog != null
                                        && body.Attachments != null;
                        if (useVision)
                        {
                            var assetIds = body.Attachments!
                                .Where(a => !string.IsNullOrWhiteSpace(a.AssetId))
                                .Select(a => a.AssetId.Trim())
                                .ToList();
                            var images = await PlaygroundPersonaMultimodalHelper
                                .LoadTurnImagesBase64Async(
                                    _visualCatalog!,
                                    _blobStore!,
                                    rootFull,
                                    scenarioResolved!,
                                    assetIds,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            if (images.Count == 0)
                            {
                                flowStream = new OllamaStreamAccumulation(
                                    "Could not load attached photo bytes for vision model.",
                                    "No image bytes");
                            }
                            else
                            {
                                var visionResult = await PlaygroundPersonaMultimodalHelper
                                    .RunVisionPersonaAsync(
                                        _visionChat!,
                                        systemPrompt: $"Agent: {pSpec.Id}\nRole: {pSpec.Role}\nName: {pSpec.Name}",
                                        userText: built,
                                        images,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                                var visionText = visionResult.Success
                                    ? (visionResult.Content ?? "")
                                    : ("Error: " + (visionResult.Error ?? "Vision model failed."));
                                if (!string.IsNullOrEmpty(visionText))
                                {
                                    await GatedWriteSseAsync(new AgentStreamEvent
                                        {
                                            Type = "llm_delta",
                                            Payload = visionText,
                                            AgentId = spec.Id
                                        })
                                        .ConfigureAwait(false);
                                }

                                flowStream = new OllamaStreamAccumulation(
                                    visionText,
                                    visionResult.Success ? null : visionResult.Error);
                            }
                        }
                        else
                        {
                            flowStream = await OllamaGenerateHttp.StreamGenerateAsync(
                                    LlmHttp,
                                    ollamaBaseFlow,
                                    modelFlow,
                                    built,
                                    async (token, _) =>
                                    {
                                        await GatedWriteSseAsync(new AgentStreamEvent
                                            {
                                                Type = "llm_delta",
                                                Payload = token,
                                                AgentId = spec.Id
                                            })
                                            .ConfigureAwait(false);
                                    },
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (Exception exStream)
                    {
                        _logger.LogWarning(exStream, "Playground flow persona stream failed");
                        flowStream = new OllamaStreamAccumulation($"Error: {exStream.Message}", exStream.Message);
                    }

                    if (flowStream.Error != null)
                        _logger.LogWarning("Playground flow stream ended with issue: {Err}", flowStream.Error);

                    try
                    {
                        personaScopeFlow?.SetTimelineDetailJson(
                            PlaygroundTraceTimelineDetail.BuildPersonaLlmJson(built, flowStream.Text, modelFlow, ollamaBaseFlow));
                    }
                    catch (Exception exDetail)
                    {
                        _logger.LogDebug(exDetail, "Playground: persona trace detail JSON skipped");
                    }
                }

                var rawFlow = flowStream.Text;
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
                            var ingestRaw = rawFlow;
                            if (MemoryIntentJson.TryRewritePlaceholderEntityKeys(
                                    rawFlow,
                                    playgroundActiveKey,
                                    out var rewritten))
                            {
                                ingestRaw = rewritten;
                            }

                            var ingestFlow = await _pipeline
                                .IngestFromExtractorOutputAsync(rootFull, scenarioResolved!, ingestRaw, cancellationToken)
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
                            lastExtractorRawOutput = rawFlow;
                            if (!ingestFlow.ParseSuccess)
                                _logger.LogWarning(
                                    "Playground flow ingest parse failed (scenario {Scenario}): {Summary}. Output prefix: {Prefix}",
                                    scenarioResolved,
                                    ingestFlow.Summary ?? "",
                                    ProjectMemoryPersonaLlmRunner.TruncateForIngestLog(rawFlow));

                            if (hasAttachments)
                            {
                                await GatedWriteFlowStepAsync(
                                        PlaygroundFlowPlanBuilder.VisualExtractStepId,
                                        "running",
                                        "Background vision analysis…")
                                    .ConfigureAwait(false);
                                await GatedWriteFlowStepAsync(
                                        PlaygroundFlowPlanBuilder.VisualExtractStepId,
                                        "done",
                                        "Vision extract queued (Gemma background pipeline)")
                                    .ConfigureAwait(false);
                            }

                            // PRD-019 Option B + F: persist active subject after the playground extractor turn
                            // so a brand-new browser session in the same scenario can still resolve "He/She"
                            // back to the right entity. Best-effort: never break the playground SSE flow here.
                            try
                            {
                                var (focusSlug, _) = ProjectMemoryCoreferenceCoordinator.ResolveFocusFromExtract(
                                    ingestRaw,
                                    playgroundActiveKey ?? corefForFlow?.ActiveSubjectKey);
                                await ApplyPlaygroundFocusPostHookAsync(
                                        rootFull,
                                        scenarioResolved,
                                        chatProject,
                                        sessionId,
                                        focusSlug,
                                        playgroundActiveDisplay ?? corefForFlow?.ActiveSubjectDisplay,
                                        "extracted",
                                        GatedWriteSseAsync,
                                        spec.Id,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception exFocus)
                            {
                                _logger.LogDebug(exFocus, "Playground: extract focus post-hook skipped");
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
                IScenarioFlowRouterLlmService flowRouter = _scenarioFlowRouterLlm;
                string? routerAppendix = null;
                if (hasAttachments)
                {
                    var routingCtx = PlaygroundFlowRoutingContextBuilder.Build(
                        body.Attachments!.Count,
                        flowUserMessage,
                        chatProject?.FocusEntityKey,
                        body.Attachments);
                    flowRouter = new PlaygroundAttachmentRouterDecorator(_scenarioFlowRouterLlm, routingCtx);
                    routerAppendix = PlaygroundFlowAttachmentRouting.BuildRoutingAppendix(routingCtx);
                }

                var interpreter = new ScenarioFlowGraphInterpreter();
                fullTextFlow = await interpreter
                    .ExecuteAsync(
                        scenarioDef.Flow!,
                        flowUserMessage,
                        invokeFlow,
                        Timeout.InfiniteTimeSpan,
                        rootFull,
                        flowRouter,
                        flowObserver,
                        ct,
                        routerAppendix)
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

            if (IngestUserMessageFormatter.ShouldPreferIngestSummary(lastExtractorIngest, personasSeen))
            {
                fullTextFlow = ProjectMemoryUiLinkFormatter.WithAbsoluteWorkspaceLinks(
                    IngestUserMessageFormatter.Format(lastExtractorIngest!, lastExtractorRawOutput, rootFull),
                    Request);
            }
            else
            {
                fullTextFlow = ProjectMemoryUiLinkFormatter.WithAbsoluteWorkspaceLinks(fullTextFlow, Request);
            }

            using (var persistScopeFlow = _activityTracker?.StartActivity("pm.playground.persist-assistant"))
            {
                try
                {
                    var transcriptPersona =
                        ScenarioFlowOutputComposer.PickTranscriptPersonaId(personasSeen) ?? lastPersona;
                    await AppendPlaygroundTurnAsync(sessionId, SessionRole.Assistant, fullTextFlow, transcriptPersona, turnGroupId, null, ct)
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
            await RunStreamVisualExtractIfNeededAsync().ConfigureAwait(false);
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
        var accText = "";
        var personaFailed = false;
        using (var personaScope = _activityTracker?.StartActivity("pm.playground.persona-llm"))
        {
            try
            {
                var streamed = await OllamaGenerateHttp.StreamGenerateAsync(
                        LlmHttp,
                        ollamaBase,
                        model,
                        prompt,
                        async (token, _) =>
                        {
                            await WriteSseAsync(new AgentStreamEvent { Type = "llm_delta", Payload = token, AgentId = spec.Id })
                                .ConfigureAwait(false);
                        },
                        ct)
                    .ConfigureAwait(false);
                accText = streamed.Text;
                if (streamed.Error != null)
                    _logger.LogWarning("Playground stream incomplete: {Err}", streamed.Error);
            }
            catch (Exception ex)
            {
                personaFailed = true;
                _logger.LogWarning(ex, "Playground stream failed");
                await WriteFlowStepAsync(runnerStep.Id, "error", ex.Message).ConfigureAwait(false);
                await WriteSseAsync(new AgentStreamEvent { Type = "error", Payload = ex.Message, AgentId = spec.Id }).ConfigureAwait(false);
                accText = string.IsNullOrEmpty(accText) ? "Error: " + ex.Message : accText;
            }
            finally
            {
                sw.Stop();
            }

            try
            {
                personaScope?.SetTimelineDetailJson(
                    PlaygroundTraceTimelineDetail.BuildPersonaLlmJson(prompt, accText, model, ollamaBase));
            }
            catch (Exception exDetail)
            {
                _logger.LogDebug(exDetail, "Playground: persona trace detail JSON skipped");
            }
        }

        var rawLlm = accText;
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
                            await AppendPlaygroundTurnAsync(sessionId, SessionRole.Assistant, fullText, agentId: spec.Id, turnGroupId, null, ct)
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
        await RunStreamVisualExtractIfNeededAsync().ConfigureAwait(false);
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
        string? attachmentsJson,
        CancellationToken cancellationToken)
    {
        var turn = new SessionTurn
        {
            SessionId = sessionId,
            TurnGroupId = turnGroupId,
            Role = role,
            Content = content,
            AgentId = agentId,
            AttachmentsJson = attachmentsJson
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
