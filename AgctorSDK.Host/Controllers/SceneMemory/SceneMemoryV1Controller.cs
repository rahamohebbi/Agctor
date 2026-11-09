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
        ["studio-instances", "explicit-entity-linking", "studio-version-checks", "studio-link-management", "prompt-preview", "preview-edit", "idempotent-apply", "shot-timeline", "continuity-context", "approved-shot-video-prompt", "shot-design-snapshots", "video-prompt-rewrite", "latest-linked-designs", "shot-deletion"]);

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

    /// <summary>Atomically change or unlink the design on an existing scene entity. A null design unlinks; scene identity and history remain intact.</summary>
    [HttpPut("workspaces/{workspaceId}/entities/{entityId}/studio-link")]
    [ProducesResponseType(typeof(SceneMemoryWorkspaceResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneMemoryWorkspaceResponse>> ChangeDesign(string workspaceId, string entityId, ChangeSceneDesignRequest body, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Workspace(await store.ChangeStudioLinkAsync(workspaceId, entityId, body.ExpectedRevision, body.InstanceKey,
            body.Design is { } d ? new(d.AssetId, d.Version, d.Name, d.Kind, d.Defaults, d.EntityId) : null, ct)));

    /// <summary>Interpret a prompt without saving story state. This request can take several minutes.</summary>
    [HttpPost("workspaces/{workspaceId}/previews")]
    [ProducesResponseType(typeof(SceneMemoryPreviewResponse), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneMemoryPreviewResponse>> Preview(string workspaceId, InterpretSceneRequest body, CancellationToken ct) => Run(async () =>
    {
        if (body.ExpectedRevision != null && ScenePromptReducer.Revision(await store.GetAsync(workspaceId, ct)) != body.ExpectedRevision)
            throw new InvalidOperationException("Story continuity changed. Refresh it and preview the prompt again. No changes were saved.");
        var review = await prompts.PreviewAsync(workspaceId, body.Prompt, body.ShotName, body.Model,
            body.SceneName, body.DurationSeconds, ct);
        if (body.ExpectedRevision != null && review.Preview.Revision != body.ExpectedRevision)
            throw new InvalidOperationException("Story continuity changed. Refresh it and preview the prompt again. No changes were saved.");
        return SceneMemoryApiProjection.Preview(review);
    });

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

    /// <summary>Delete one shot and its continuity changes. Other shot actions and design links are preserved.</summary>
    [HttpDelete("workspaces/{workspaceId}/shots/{shotId}")]
    public Task<ActionResult<SceneTimelineResponse>> DeleteShot(string workspaceId, string shotId,
        [FromBody] PrepareSceneVideoRequest body, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Timeline(await store.DeleteShotAsync(workspaceId, shotId, body.ExpectedRevision, ct)));

    /// <summary>Inspect the saved state and design references used for the next shot.</summary>
    [HttpGet("workspaces/{workspaceId}/context")]
    public Task<ActionResult<SceneContinuityResponse>> Context(string workspaceId, CancellationToken ct) => Run(async () =>
        SceneVideoPromptBuilder.Context(await store.GetAsync(workspaceId, ct)));

    /// <summary>Prepare a video composer draft from an applied shot, without a model call or state mutation.</summary>
    [HttpPost("workspaces/{workspaceId}/shots/{shotId}/video-prompt")]
    public Task<ActionResult<SceneVideoPromptResponse>> VideoPrompt(string workspaceId, string shotId, PrepareSceneVideoRequest body, CancellationToken ct) => Run(async () =>
        SceneVideoPromptBuilder.Prepare(await store.GetAsync(workspaceId, ct), shotId, body.ExpectedRevision));

    /// <summary>Simulate Cynelo H3 prompt formatting without invoking a video model or changing the story.</summary>
    [HttpPost("workspaces/{workspaceId}/shots/{shotId}/video-prompt/simulate")]
    public Task<ActionResult<SceneVideoPromptSimulationResponse>> SimulateVideoPrompt(string workspaceId, string shotId, SimulateSceneVideoRequest body, CancellationToken ct) => Run(async () =>
        CyneloH3PromptSimulation.Prepare(SceneVideoPromptBuilder.Prepare(await store.GetAsync(workspaceId, ct), shotId, body.ExpectedRevision), body));

    /// <summary>Installed chat models suitable for creative prompt editing.</summary>
    [HttpGet("prompt-models")]
    public Task<ActionResult<string[]>> PromptModels([FromServices] Services.Models.ModelCapabilitiesService models, CancellationToken ct) =>
        Run(async () => (await models.ListAsync(ct)).Where(m => m.Completion).Select(m => m.Name).ToArray());

    /// <summary>Suggest a creative edit for one video attempt. Does not change the shot or its designs.</summary>
    [HttpPost("workspaces/{workspaceId}/shots/{shotId}/video-prompt/rewrite")]
    public Task<ActionResult<RewriteVideoPromptResponse>> RewriteVideoPrompt(string workspaceId, string shotId,
        RewriteSceneVideoRequest body, [FromServices] IHttpClientFactory clients, CancellationToken ct) => Run(async () =>
    {
        SceneVideoPromptBuilder.Prepare(await store.GetAsync(workspaceId, ct), shotId, body.ExpectedRevision);
        using var http = clients.CreateClient(OllamaScenePromptInterpreter.ClientName);
        var suggestion = await SceneVideoPromptRewrite.RewriteAsync(new(body.Prompt, body.Instruction, body.Model), http, ct);
        // Do not return a suggestion based on a shot that changed while the model was working.
        SceneVideoPromptBuilder.Prepare(await store.GetAsync(workspaceId, ct), shotId, body.ExpectedRevision);
        return suggestion;
    }, "Ollama took too long to revise the prompt. Try a smaller edit or another model. Your prompt is unchanged.");

    /// <summary>Compare saved shot designs with the current story bindings.</summary>
    [HttpGet("workspaces/{workspaceId}/shots/{shotId}/designs")]
    public Task<ActionResult<SceneShotDesignReview>> ShotDesigns(string workspaceId, string shotId, CancellationToken ct) => Run(async () =>
        SceneShotDesigns.Review(await store.GetAsync(workspaceId, ct), shotId));

    /// <summary>Pin current story designs to this shot only. Facts and other shots stay intact.</summary>
    [HttpPut("workspaces/{workspaceId}/shots/{shotId}/designs")]
    public Task<ActionResult<SceneTimelineResponse>> PinDesigns(string workspaceId, string shotId, PrepareSceneVideoRequest body, CancellationToken ct) => Run(async () =>
        SceneMemoryApiProjection.Timeline(await store.PinShotDesignsAsync(workspaceId, shotId, body.ExpectedRevision, ct)));

    /// <summary>Read current or historical facts for an entity, including reverse possession relationships.</summary>
    [HttpPost("workspaces/{workspaceId}/query")]
    [ProducesResponseType(typeof(SceneQueryResult), StatusCodes.Status200OK)]
    public Task<ActionResult<SceneQueryResult>> Query(string workspaceId, SceneQuery body, CancellationToken ct) => Run(() => store.QueryAsync(workspaceId, body, ct));

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action, string? timeoutMessage = null)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return Failure(404, "not_found", ex.Message); }
        catch (ScenePromptInterpretationException ex) { return Failure(422, "interpretation_failed", ex.Message, ex.Attempts); }
        catch (ScenePromptValidationException ex) { return Failure(422, "invalid_scene_changes", ex.Message, ex.Issues); }
        catch (ArgumentException ex) { return Failure(400, "invalid_request", ex.Message); }
        catch (InvalidOperationException ex) { return Failure(409, "conflict", ex.Message); }
        catch (HttpRequestException) { return Failure(502, "model_unavailable", "The model could not be reached. Check Ollama and the selected chat model."); }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        { return Failure(504, "interpretation_timeout", timeoutMessage ?? "Interpretation timed out. No changes were saved."); }
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
