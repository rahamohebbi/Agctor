using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Host.Models.SceneMemory;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers.SceneMemory;

/// <summary>Scene Memory integration API. Independent of the dashboard, test agents and other AGCTOR APIs.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "scene-memory-v1")]
[Route("api/scene-memory/v1")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
public sealed class SceneMemoryV1Controller(ISceneMemoryStore store, ScenePromptService prompts) : ControllerBase
{
    /// <summary>Discover the supported integration contract and its current persistence limits.</summary>
    [HttpGet("capabilities")]
    [ProducesResponseType(typeof(SceneMemoryCapabilitiesResponse), StatusCodes.Status200OK)]
    public ActionResult<SceneMemoryCapabilitiesResponse> Capabilities() => new SceneMemoryCapabilitiesResponse("1",
        ["Character", "Object", "Location"], false, 20,
        ["studio-instances", "explicit-entity-linking", "studio-version-checks", "prompt-preview", "preview-edit", "idempotent-apply", "shot-timeline"]);

    /// <summary>List connected stories in one external application/project namespace. The caller must authorize that namespace.</summary>
    [HttpGet("workspaces")]
    [ProducesResponseType(typeof(SceneMemoryWorkspaceResponse[]), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneMemoryWorkspaceResponse[]>> List([FromQuery] string application, [FromQuery] string projectId, CancellationToken ct) => Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(application) || application.Length > 100 || string.IsNullOrWhiteSpace(projectId) || projectId.Length > 200)
            throw new ArgumentException("Application and projectId are required.");
        return (await store.ListAsync(ct)).Where(w => w.Connection?.Application == application && w.Connection.ProjectId == projectId)
            .Select(SceneMemoryApiProjection.Workspace).ToArray();
    });

    /// <summary>Idempotently connect an external project/story. Repeating the same identity returns the same workspace.</summary>
    [HttpPost("workspaces/connect")]
    [ProducesResponseType(typeof(SceneMemoryWorkspaceResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneMemoryWorkspaceResponse>> Connect(ConnectStoryRequest body, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Workspace(await store.ConnectAsync(body.Name, new(body.Application, body.ProjectId, body.StoryId), body.WorkspaceId, ct)));

    /// <summary>Read identities, Studio bindings and the revision used to detect stale previews.</summary>
    [HttpGet("workspaces/{workspaceId}")]
    [ProducesResponseType(typeof(SceneMemoryWorkspaceResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneMemoryWorkspaceResponse>> Get(string workspaceId, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Workspace(await store.GetAsync(workspaceId, ct)));

    /// <summary>Upsert a Character/Object/Location Studio instance. Supply entityId to link a discovered entity explicitly.</summary>
    /// <remarks>Defaults are design metadata, not state mutations. Same-version identical retries are safe. Stale or conflicting versions return 409.</remarks>
    [HttpPut("workspaces/{workspaceId}/studio-entities/{instanceKey}")]
    [ProducesResponseType(typeof(SceneMemoryWorkspaceResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneMemoryWorkspaceResponse>> Sync(string workspaceId, string instanceKey, SyncStudioRequest body, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Workspace(await store.SyncStudioAsync(workspaceId, instanceKey,
            new(body.AssetId, body.Version, body.Name, body.Kind, body.Defaults, body.EntityId), ct)));

    /// <summary>Interpret a prompt without saving story state. This request can take several minutes.</summary>
    [HttpPost("workspaces/{workspaceId}/previews")]
    [ProducesResponseType(typeof(SceneMemoryPreviewResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneMemoryPreviewResponse>> Preview(string workspaceId, InterpretSceneRequest body, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Preview(await prompts.PreviewAsync(workspaceId, body.Prompt, body.ShotName, body.Model,
            body.SceneName, body.DurationSeconds, ct)));

    /// <summary>Validate human corrections to an unexpired preview. Story state remains unchanged.</summary>
    [HttpPut("workspaces/{workspaceId}/previews/{previewId}")]
    [ProducesResponseType(typeof(SceneMemoryPreviewResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneMemoryPreviewResponse>> Edit(string workspaceId, string previewId, EditScenePreviewRequest body, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Preview(await prompts.EditAsync(workspaceId, previewId, body.Extraction, ct)));

    /// <summary>Atomically apply a reviewed preview. Retrying an applied preview does not duplicate history.</summary>
    [HttpPost("workspaces/{workspaceId}/previews/{previewId}/apply")]
    [ProducesResponseType(typeof(SceneMemoryWorkspaceResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneMemoryWorkspaceResponse>> Apply(string workspaceId, string previewId, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Workspace(await prompts.ApplyAsync(workspaceId, previewId, ct)));

    /// <summary>Get ordered shots, before/after changes, story moments and per-shot state snapshots for your own UI.</summary>
    [HttpGet("workspaces/{workspaceId}/timeline")]
    [ProducesResponseType(typeof(SceneTimelineResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneTimelineResponse>> Timeline(string workspaceId, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Timeline(await store.GetAsync(workspaceId, ct)));

    /// <summary>Read current or historical facts for an entity, including reverse possession relationships.</summary>
    [HttpPost("workspaces/{workspaceId}/query")]
    [ProducesResponseType(typeof(SceneQueryResult), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneQueryResult>> Query(string workspaceId, SceneQuery body, CancellationToken ct) => Run(() => store.QueryAsync(workspaceId, body, ct));

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return Failure(404, "not_found", ex.Message); }
        catch (ScenePromptInterpretationException ex) { return Failure(422, "interpretation_failed", ex.Message, ex.Attempts); }
        catch (ScenePromptValidationException ex) { return Failure(422, "invalid_scene_changes", ex.Message, ex.Issues); }
        catch (ArgumentException ex) { return Failure(400, "invalid_request", ex.Message); }
        catch (InvalidOperationException ex) { return Failure(409, "conflict", ex.Message); }
        catch (HttpRequestException) { return Failure(502, "model_unavailable", "The interpretation model could not be reached."); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { return Failure(504, "interpretation_timeout", "Interpretation timed out. No changes were saved."); }
    }

    private ObjectResult Failure(int status, string code, string detail, object? diagnostics = null)
    {
        var problem = new ProblemDetails { Status = status, Title = code, Detail = detail, Instance = Request.Path };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        if (diagnostics != null) problem.Extensions["diagnostics"] = diagnostics;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
