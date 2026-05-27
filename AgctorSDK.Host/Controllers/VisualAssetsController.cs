using System.Text.Json;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Visual;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Controllers;

/// <summary>PRD-023a/023c: visual uploads via <c>person-visual-ingest</c> tool + catalog reads.</summary>
[ApiController]
[Route("api/visual/assets")]
[Produces("application/json")]
public sealed class VisualAssetsController : ControllerBase
{
    private readonly VisualIngestToolBridge _ingest;
    private readonly IBlobStore _blobs;
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions> _projectOptions;
    private readonly VisualStorageOptions _visualOptions;
    private readonly ILogger<VisualAssetsController> _logger;

    private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

    public VisualAssetsController(
        VisualIngestToolBridge ingest,
        IBlobStore blobs,
        IOptionsMonitor<ProjectMemoryAgentOptions> projectOptions,
        IOptions<VisualStorageOptions> visualOptions,
        ILogger<VisualAssetsController> logger)
    {
        _ingest = ingest ?? throw new ArgumentNullException(nameof(ingest));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        _projectOptions = projectOptions ?? throw new ArgumentNullException(nameof(projectOptions));
        _visualOptions = visualOptions?.Value ?? new VisualStorageOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("init-upload")]
    [ProducesResponseType(typeof(VisualAssetInitUploadResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VisualAssetInitUploadResponseDto>> InitUploadAsync(
        [FromBody] VisualAssetInitUploadRequestDto? body,
        CancellationToken cancellationToken)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.ScenarioId))
            return BadRequest(new ErrorResponse { Code = "SCENARIO_REQUIRED", Message = "scenarioId is required." });

        var root = ResolveProjectRoot(body.ProjectRoot);
        if (root.Error != null)
            return root.Error;

        var (ok, json, err) = await _ingest.InvokeAsync(
                "InitUpload",
                new Dictionary<string, object>
                {
                    ["projectRoot"] = root.Root!,
                    ["scenarioId"] = body.ScenarioId.Trim(),
                    ["contentType"] = body.ContentType ?? "image/jpeg",
                    ["bytes"] = body.Bytes,
                    ["sessionId"] = body.SessionId ?? "",
                    ["turnGroupId"] = body.TurnGroupId ?? ""
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!ok || json == null)
            return BadRequest(new ErrorResponse { Code = "INIT_UPLOAD_FAILED", Message = err ?? "Init failed." });

        var init = JsonSerializer.Deserialize<InitUploadToolPayload>(json.Value.GetRawText(), JsonRead);
        if (init == null || string.IsNullOrWhiteSpace(init.AssetId))
            return BadRequest(new ErrorResponse { Code = "INIT_UPLOAD_FAILED", Message = "Invalid tool response." });

        var mode = string.Equals(_visualOptions.Provider, "file", StringComparison.OrdinalIgnoreCase) ? "file" : "s3";
        var uploadUrl = init.UploadUrl ?? "";
        if (mode == "file")
        {
            uploadUrl =
                $"{Request.Scheme}://{Request.Host}/api/visual/assets/{init.AssetId}/raw?scenarioId={Uri.EscapeDataString(body.ScenarioId.Trim())}";
        }

        return Ok(new VisualAssetInitUploadResponseDto
        {
            AssetId = init.AssetId,
            UploadUrl = uploadUrl,
            UploadHeaders = init.UploadHeaders ?? new(),
            ExpiresAt = init.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(15),
            UploadMode = mode
        });
    }

    [HttpPost("{assetId}/complete")]
    [ProducesResponseType(typeof(VisualAssetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VisualAssetDto>> CompleteAsync(
        [FromRoute] string assetId,
        [FromBody] VisualAssetCompleteUploadRequestDto? body,
        CancellationToken cancellationToken)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.ScenarioId))
            return BadRequest(new ErrorResponse { Code = "SCENARIO_REQUIRED", Message = "scenarioId is required." });

        var root = ResolveProjectRoot(body.ProjectRoot);
        if (root.Error != null)
            return root.Error;

        var (ok, json, err) = await _ingest.InvokeAsync(
                "CompleteUpload",
                new Dictionary<string, object>
                {
                    ["projectRoot"] = root.Root!,
                    ["scenarioId"] = body.ScenarioId.Trim(),
                    ["assetId"] = assetId,
                    ["sha256"] = body.Sha256 ?? ""
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!ok || json == null)
            return BadRequest(new ErrorResponse { Code = "COMPLETE_FAILED", Message = err ?? "Complete failed." });

        var record = JsonSerializer.Deserialize<VisualAssetRecord>(json.Value.GetRawText(), JsonRead);
        if (record == null)
            return BadRequest(new ErrorResponse { Code = "COMPLETE_FAILED", Message = "Invalid tool response." });

        return Ok(await ToDtoAsync(record, root.Root!, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Manual subject tag from playground clarify chips (PRD-023f).</summary>
    [HttpPost("{assetId}/annotate")]
    [ProducesResponseType(typeof(VisualAssetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VisualAssetDto>> AnnotateAsync(
        [FromRoute] string assetId,
        [FromBody] VisualAssetAnnotateRequestDto? body,
        CancellationToken cancellationToken)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.ScenarioId) || string.IsNullOrWhiteSpace(body.EntityKey))
            return BadRequest(new ErrorResponse { Code = "ANNOTATE_INVALID", Message = "scenarioId and entityKey are required." });

        var root = ResolveProjectRoot(body.ProjectRoot);
        if (root.Error != null)
            return root.Error;

        var subjects = JsonSerializer.Serialize(new[]
        {
            new VisualAssetSubject
            {
                EntityKey = body.EntityKey.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(body.DisplayName) ? null : body.DisplayName.Trim(),
                Role = "primary"
            }
        });

        var (ok, json, err) = await _ingest.InvokeAsync(
                "Annotate",
                new Dictionary<string, object>
                {
                    ["projectRoot"] = root.Root!,
                    ["scenarioId"] = body.ScenarioId.Trim(),
                    ["assetId"] = assetId,
                    ["subjects"] = subjects
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!ok || json == null)
            return BadRequest(new ErrorResponse { Code = "ANNOTATE_FAILED", Message = err ?? "Annotate failed." });

        var record = JsonSerializer.Deserialize<VisualAssetRecord>(json.Value.GetRawText(), JsonRead);
        if (record == null)
            return BadRequest(new ErrorResponse { Code = "ANNOTATE_FAILED", Message = "Invalid tool response." });

        return Ok(await ToDtoAsync(record, root.Root!, cancellationToken).ConfigureAwait(false));
    }

    [HttpPut("{assetId}/raw")]
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadRawAsync(
        [FromRoute] string assetId,
        [FromQuery] string scenarioId,
        [FromQuery] string? projectRoot,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_visualOptions.Provider, "file", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ErrorResponse { Code = "RAW_UPLOAD_DISABLED", Message = "Raw upload is only for Agctor:Visual:Provider=file." });

        if (string.IsNullOrWhiteSpace(scenarioId))
            return BadRequest(new ErrorResponse { Code = "SCENARIO_REQUIRED", Message = "scenarioId query is required." });

        var root = ResolveProjectRoot(projectRoot);
        if (root.Error != null)
            return root.Error;

        var (ok, json, err) = await _ingest.InvokeAsync(
                "GetAsset",
                new Dictionary<string, object>
                {
                    ["projectRoot"] = root.Root!,
                    ["scenarioId"] = scenarioId,
                    ["assetId"] = assetId
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!ok || json == null)
            return NotFound(new ErrorResponse { Code = "ASSET_NOT_FOUND", Message = err ?? $"Asset '{assetId}' not found." });

        var payload = JsonSerializer.Deserialize<GetAssetToolPayload>(json.Value.GetRawText(), JsonRead);
        var record = payload?.Asset;
        if (record == null)
            return NotFound(new ErrorResponse { Code = "ASSET_NOT_FOUND", Message = $"Asset '{assetId}' not found." });

        if (_blobs is not FileSystemBlobStore fileStore)
            return BadRequest(new ErrorResponse { Code = "INVALID_STORE", Message = "File provider misconfigured." });

        await fileStore.WriteObjectAsync(record.Storage.Bucket, record.Storage.Key, Request.Body, cancellationToken)
            .ConfigureAwait(false);

        return NoContent();
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<VisualAssetDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<VisualAssetDto>>> ListAsync(
        [FromQuery] string scenarioId,
        [FromQuery] string? projectRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return BadRequest(new ErrorResponse { Code = "SCENARIO_REQUIRED", Message = "scenarioId is required." });

        var root = ResolveProjectRoot(projectRoot);
        if (root.Error != null)
            return root.Error;

        var catalog = HttpContext.RequestServices.GetRequiredService<VisualAssetCatalogStore>();
        var list = await catalog.ListAsync(root.Root!, scenarioId, cancellationToken).ConfigureAwait(false);
        var dtos = new List<VisualAssetDto>();
        foreach (var asset in list)
            dtos.Add(await ToDtoAsync(asset, root.Root!, cancellationToken).ConfigureAwait(false));

        return Ok(dtos);
    }

    /// <summary>Streams image bytes for playground transcript and inline previews (required for file provider).</summary>
    [HttpGet("{assetId}/view")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ViewAsync(
        [FromRoute] string assetId,
        [FromQuery] string scenarioId,
        [FromQuery] string? projectRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return BadRequest(new ErrorResponse { Code = "SCENARIO_REQUIRED", Message = "scenarioId is required." });

        var root = ResolveProjectRoot(projectRoot);
        if (root.Error != null)
            return root.Error;

        var (ok, json, err) = await _ingest.InvokeAsync(
                "GetAsset",
                new Dictionary<string, object>
                {
                    ["projectRoot"] = root.Root!,
                    ["scenarioId"] = scenarioId.Trim(),
                    ["assetId"] = assetId
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!ok || json == null)
            return NotFound(new ErrorResponse { Code = "ASSET_NOT_FOUND", Message = err ?? $"Asset '{assetId}' not found." });

        var payload = JsonSerializer.Deserialize<GetAssetToolPayload>(json.Value.GetRawText(), JsonRead);
        var record = payload?.Asset;
        if (record == null)
            return NotFound(new ErrorResponse { Code = "ASSET_NOT_FOUND", Message = $"Asset '{assetId}' not found." });

        try
        {
            var bytes = await _blobs
                .ReadObjectBytesAsync(record.Storage.Bucket, record.Storage.Key, cancellationToken)
                .ConfigureAwait(false);
            var contentType = string.IsNullOrWhiteSpace(record.Storage.ContentType)
                ? "image/jpeg"
                : record.Storage.ContentType;
            return File(bytes, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "View failed for visual asset {AssetId}", assetId);
            return NotFound(new ErrorResponse { Code = "BLOB_NOT_FOUND", Message = "Visual blob not found." });
        }
    }

    [HttpGet("{assetId}")]
    [ProducesResponseType(typeof(VisualAssetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VisualAssetDto>> GetAsync(
        [FromRoute] string assetId,
        [FromQuery] string scenarioId,
        [FromQuery] string? projectRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return BadRequest(new ErrorResponse { Code = "SCENARIO_REQUIRED", Message = "scenarioId is required." });

        var root = ResolveProjectRoot(projectRoot);
        if (root.Error != null)
            return root.Error;

        var (ok, json, err) = await _ingest.InvokeAsync(
                "GetAsset",
                new Dictionary<string, object>
                {
                    ["projectRoot"] = root.Root!,
                    ["scenarioId"] = scenarioId,
                    ["assetId"] = assetId
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!ok || json == null)
            return NotFound(new ErrorResponse { Code = "ASSET_NOT_FOUND", Message = err ?? $"Asset '{assetId}' not found." });

        var payload = JsonSerializer.Deserialize<GetAssetToolPayload>(json.Value.GetRawText(), JsonRead);
        if (payload?.Asset == null)
            return NotFound(new ErrorResponse { Code = "ASSET_NOT_FOUND", Message = $"Asset '{assetId}' not found." });

        var dto = await ToDtoAsync(payload.Asset, root.Root!, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(payload.ViewUrl))
        {
            dto.ViewUrl = payload.ViewUrl;
            dto.ViewUrlExpiresAt = payload.ViewUrlExpiresAt;
        }

        return Ok(dto);
    }

    private async Task<VisualAssetDto> ToDtoAsync(VisualAssetRecord record, string projectRoot, CancellationToken cancellationToken)
    {
        var dto = new VisualAssetDto
        {
            AssetId = record.AssetId,
            ScenarioId = record.ScenarioId,
            State = record.State,
            ContentType = record.Storage.ContentType,
            Bytes = record.Storage.Bytes,
            StatusDetail = VisualAssetStatusDetail.ForRecord(record),
            InferenceConfidence = record.Inference?.Confidence,
            SubjectEntityKeys = record.Subjects
                .Select(s => s.EntityKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        if (string.Equals(record.State, VisualAssetStates.Uploaded, StringComparison.OrdinalIgnoreCase)
            || string.Equals(record.State, VisualAssetStates.Ready, StringComparison.OrdinalIgnoreCase)
            || string.Equals(record.State, VisualAssetStates.ReadyForExtract, StringComparison.OrdinalIgnoreCase)
            || string.Equals(record.State, VisualAssetStates.Extracted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(record.State, VisualAssetStates.InboxPending, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (string.Equals(_visualOptions.Provider, "file", StringComparison.OrdinalIgnoreCase))
                {
                    dto.ViewUrl = VisualAssetViewUrls.Build(record.AssetId, record.ScenarioId, projectRoot);
                    dto.ViewUrlExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                        Math.Max(60, _visualOptions.PresignedViewExpirySeconds));
                }
                else
                {
                    var expiry = TimeSpan.FromSeconds(Math.Max(60, _visualOptions.PresignedViewExpirySeconds));
                    var access = await _blobs.CreatePresignedGetAsync(
                        record.Storage.Bucket,
                        record.Storage.Key,
                        expiry,
                        cancellationToken).ConfigureAwait(false);
                    dto.ViewUrl = access.Url;
                    dto.ViewUrlExpiresAt = access.ExpiresAt;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "View URL failed for asset {AssetId}", record.AssetId);
            }
        }

        return dto;
    }

    private (string? Root, ActionResult? Error) ResolveProjectRoot(string? overrideRoot)
    {
        var root = !string.IsNullOrWhiteSpace(overrideRoot)
            ? overrideRoot.Trim()
            : _projectOptions.CurrentValue.ProjectRoot?.Trim();

        if (string.IsNullOrWhiteSpace(root))
        {
            return (null, BadRequest(new ErrorResponse
            {
                Code = "PROJECT_ROOT_REQUIRED",
                Message = "Set Agctor:ProjectMemory:ProjectRoot or pass projectRoot."
            }));
        }

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            return (null, NotFound(new ErrorResponse { Code = "PROJECT_NOT_FOUND", Message = $"Project root '{root}' not found." }));

        if (!System.IO.File.Exists(Path.Combine(root, ".agctor", "project.yaml")))
        {
            return (null, BadRequest(new ErrorResponse
            {
                Code = "INVALID_PROJECT",
                Message = "Folder must contain .agctor/project.yaml."
            }));
        }

        return (root, null);
    }

    private sealed class InitUploadToolPayload
    {
        public string? AssetId { get; set; }
        public string? UploadUrl { get; set; }
        public Dictionary<string, string>? UploadHeaders { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private sealed class GetAssetToolPayload
    {
        public VisualAssetRecord? Asset { get; set; }
        public string? ViewUrl { get; set; }
        public DateTimeOffset? ViewUrlExpiresAt { get; set; }
    }
}
