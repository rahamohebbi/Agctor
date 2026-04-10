using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Net.Http.Json;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Core.Streaming;
using AgctorSDK.Core.ProjectMemory.Indexing;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Validation;
using AgctorSDK.Core.ProjectMemory.Yaml;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
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
    private readonly ILogger<ProjectMemoryController> _logger;
    private static readonly HttpClient LlmHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonSse = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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
        ILogger<ProjectMemoryController> logger)
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
        var dto = new ProjectMemoryStatusDto { ProjectRoot = root ?? "" };
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

        IReadOnlyList<SessionTurn>? prior = null;
        if (!string.IsNullOrWhiteSpace(body.SessionId))
            prior = await _sessions.GetTurnsAsync(body.SessionId.Trim(), null, cancellationToken).ConfigureAwait(false);

        var prompt = BuildPlaygroundPrompt(spec, prior, body.InputText);
        var sw = Stopwatch.StartNew();
        var output = await CallLocalLlmAsync(prompt, cancellationToken).ConfigureAwait(false);
        sw.Stop();

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
            ConversationPrefix = conversationPrefix
        };

        var result = await _pipeline.RunAsync(req, cancellationToken).ConfigureAwait(false);
        return Ok(new ProjectMemoryOrchestratorRunResponseDto
        {
            CorrelationId = result.CorrelationId,
            Success = result.Success,
            FinalText = result.FinalText,
            Steps = result.Steps.Select(s => new ProjectMemoryOrchestratorStepDto
            {
                Name = s.Name,
                Ok = s.Ok,
                Detail = s.Detail,
                UpdatedFiles = s.UpdatedFiles
            }).ToList()
        });
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

        var prompt = BuildPlaygroundPrompt(spec, prior, body.Payload);
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

        async Task WriteSseAsync(AgentStreamEvent evt)
        {
            var json = JsonSerializer.Serialize(evt, JsonSse);
            await Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }

        await WriteSseAsync(new AgentStreamEvent
        {
            Type = "phase",
            Payload = $"Running {spec.Id}…",
            AgentId = spec.Id
        }).ConfigureAwait(false);

        var acc = new StringBuilder();
        try
        {
            var ollama = LLMAgent.GetConfiguredOllamaApiUrl().TrimEnd('/') + "/";
            var model = LLMAgent.GetConfiguredDefaultModel();
            using var req = new HttpRequestMessage(HttpMethod.Post, ollama + "api/generate");
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
                    await WriteSseAsync(new AgentStreamEvent { Type = "llm_delta", Payload = token, AgentId = spec.Id }).ConfigureAwait(false);
                }

                if (done)
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playground stream failed");
            await WriteSseAsync(new AgentStreamEvent { Type = "error", Payload = ex.Message, AgentId = spec.Id }).ConfigureAwait(false);
            if (acc.Length == 0)
                acc.Append("Error: ").Append(ex.Message);
        }

        var fullText = acc.ToString();
        try
        {
            await AppendPlaygroundTurnAsync(sessionId, SessionRole.Assistant, fullText, agentId: spec.Id, turnGroupId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playground: append assistant turn failed");
        }

        var donePayload = new
        {
            type = "done",
            status = "Success",
            responseData = fullText,
            traceId = (string?)null,
            errorMessage = (string?)null,
            messageId,
            sessionId
        };
        await Response.WriteAsync("data: " + JsonSerializer.Serialize(donePayload, JsonSse) + "\n\n", ct).ConfigureAwait(false);
        await Response.Body.FlushAsync(ct).ConfigureAwait(false);
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
        return Ok(new { saved = true, projectRoot = path, note = "Restart the Host to apply configuration from appsettings.User.json unless options reload." });
    }

    [HttpGet("tree")]
    public ActionResult<TreeNodeDto> GetTree([FromQuery] int maxDepth = 4)
    {
        var root = RootOrNull();
        if (root == null)
            return BadRoot();

        var n = 0;
        var node = BuildTree(root, root, "", 0, Math.Clamp(maxDepth, 1, 8), ref n);
        return Ok(node);
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
        const int maxNodes = 400;
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

    private static string BuildPlaygroundPrompt(AgentDefinitionSpec spec, IReadOnlyList<SessionTurn>? priorTurns, string newUserText)
    {
        var lines = (spec.Instructions ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        var specHeader = $"Agent: {spec.Id}\nRole: {spec.Role}\nName: {spec.Name}\n";
        var outputHint = spec.Output?.Type?.Contains("intent", StringComparison.OrdinalIgnoreCase) == true
            ? "Return valid JSON only. Do not wrap JSON in markdown fences."
            : "Respond in plain text unless JSON is explicitly required by the instructions.";

        var sb = new StringBuilder();
        sb.Append(string.Join('\n', lines));
        sb.Append("\n\n").Append(specHeader).Append(outputHint);

        if (priorTurns is { Count: > 0 })
        {
            sb.Append("\n\n---\nConversation so far:\n");
            foreach (var t in priorTurns.OrderBy(x => x.Sequence))
            {
                if (t.Role is SessionRole.System or SessionRole.Tool)
                    continue;
                var label = t.Role == SessionRole.User ? "User" : "Assistant";
                sb.Append(label).Append(": ").Append(t.Content).Append('\n');
            }
        }

        sb.Append("\n---\nLatest user message:\n").Append(newUserText);
        return sb.ToString();
    }

    private static async Task<string> CallLocalLlmAsync(string prompt, CancellationToken cancellationToken)
    {
        var ollama = LLMAgent.GetConfiguredOllamaApiUrl().TrimEnd('/') + "/";
        var model = LLMAgent.GetConfiguredDefaultModel();
        var req = new { model, prompt, stream = false };
        var resp = await LlmHttp.PostAsJsonAsync(ollama + "api/generate", req, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<OllamaGenDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc?.response?.Trim() ?? "";
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

    private sealed class OllamaGenDto
    {
        public string? response { get; set; }
    }
}
