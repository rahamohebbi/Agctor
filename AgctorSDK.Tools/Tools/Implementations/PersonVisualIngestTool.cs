using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Actors;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.Tools.Implementations;

/// <summary>
/// Visual asset ingest: presigned upload, catalog updates, annotation, and turn linking (PRD-023c).
/// HTTP upload endpoints delegate here via <c>person-visual-ingest</c>.
/// </summary>
[AgctorHostTool(
    "person-visual-ingest",
    "Person visual ingest",
    "Upload and annotate scenario-scoped photos (InitUpload, CompleteUpload, Annotate, InferFromPrompt, LinkToTurn, GetAsset, DeleteAsset).",
    DefaultOperation = "InitUpload")]
public sealed class PersonVisualIngestTool : ToolActorBase
{
    public PersonVisualIngestTool(string id) : base(id, nameof(PersonVisualIngestTool))
    {
    }

    protected override Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(new ToolResult
        {
            IsSuccess = false,
            Error = "PersonVisualIngestTool expects a ToolRequest with a supported Operation."
        });

    public override async Task<ToolResult> Handle(ToolRequest request)
    {
        var op = request.Operation?.Trim() ?? "";
        var p = request.Parameters ?? new Dictionary<string, object>();

        try
        {
            return op.ToLowerInvariant() switch
            {
                "initupload" => await InitUploadAsync(p, CancellationToken.None).ConfigureAwait(false),
                "completeupload" => await CompleteUploadAsync(p, CancellationToken.None).ConfigureAwait(false),
                "getasset" => await GetAssetAsync(p, CancellationToken.None).ConfigureAwait(false),
                "annotate" => await AnnotateAsync(p, CancellationToken.None).ConfigureAwait(false),
                "inferfromprompt" => await InferFromPromptAsync(p, CancellationToken.None).ConfigureAwait(false),
                "linktoturn" => await LinkToTurnAsync(p, CancellationToken.None).ConfigureAwait(false),
                "deleteasset" => await DeleteAssetAsync(p, CancellationToken.None).ConfigureAwait(false),
                _ => new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" }
            };
        }
        catch (Exception ex)
        {
            return new ToolResult { IsSuccess = false, Error = ex.Message };
        }
    }

    private static async Task<ToolResult> InitUploadAsync(
        IDictionary<string, object> p,
        CancellationToken cancellationToken)
    {
        var uploads = ProjectMemoryServiceAccessor.GetRequiredService<IVisualAssetUploadService>();
        var root = VisualToolParams.ResolveProjectRoot(p);
        var scenarioId = VisualToolParams.RequireScenarioId(p);
        var mime = VisualToolParams.GetString(p, "contentType") ?? "image/jpeg";
        var bytes = VisualToolParams.GetInt64(p, "bytes");
        var sessionId = VisualToolParams.GetString(p, "sessionId");
        var turnGroupId = VisualToolParams.GetString(p, "turnGroupId");

        var result = await uploads.InitUploadAsync(
                new VisualAssetInitUploadRequest(root, scenarioId, mime, bytes, sessionId, turnGroupId),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
            return new ToolResult { IsSuccess = false, Error = result.Error ?? "InitUpload failed." };

        return new ToolResult
        {
            IsSuccess = true,
            Output = VisualToolParams.ToJson(new
            {
                assetId = result.AssetId,
                uploadUrl = result.UploadUrl,
                uploadHeaders = result.UploadHeaders,
                expiresAt = result.ExpiresAt
            })
        };
    }

    private static async Task<ToolResult> CompleteUploadAsync(
        IDictionary<string, object> p,
        CancellationToken cancellationToken)
    {
        var uploads = ProjectMemoryServiceAccessor.GetRequiredService<IVisualAssetUploadService>();
        var root = VisualToolParams.ResolveProjectRoot(p);
        var scenarioId = VisualToolParams.RequireScenarioId(p);
        var assetId = VisualToolParams.RequireAssetId(p);
        var sha256 = VisualToolParams.GetString(p, "sha256");

        var result = await uploads.CompleteUploadAsync(
                new VisualAssetCompleteUploadRequest(root, scenarioId, assetId, sha256),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success || result.Asset == null)
            return new ToolResult { IsSuccess = false, Error = result.Error ?? "CompleteUpload failed." };

        return new ToolResult { IsSuccess = true, Output = VisualToolParams.ToJson(result.Asset) };
    }

    private static async Task<ToolResult> GetAssetAsync(
        IDictionary<string, object> p,
        CancellationToken cancellationToken)
    {
        var uploads = ProjectMemoryServiceAccessor.GetRequiredService<IVisualAssetUploadService>();
        var blobs = ProjectMemoryServiceAccessor.GetRequiredService<IBlobStore>();
        var options = ProjectMemoryServiceAccessor.GetRequiredService<IOptions<VisualStorageOptions>>().Value;

        var root = VisualToolParams.ResolveProjectRoot(p);
        var scenarioId = VisualToolParams.RequireScenarioId(p);
        var assetId = VisualToolParams.RequireAssetId(p);

        var record = await uploads.GetAssetAsync(root, scenarioId, assetId, cancellationToken).ConfigureAwait(false);
        if (record == null)
            return new ToolResult { IsSuccess = false, Error = "asset_not_found" };

        if (string.Equals(record.State, VisualAssetStates.Deleted, StringComparison.OrdinalIgnoreCase))
            return new ToolResult { IsSuccess = false, Error = "asset_not_found" };

        string? viewUrl = null;
        DateTimeOffset? expiresAt = null;
        if (!string.Equals(record.State, VisualAssetStates.PendingUpload, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var expiry = TimeSpan.FromSeconds(Math.Max(60, options.PresignedViewExpirySeconds));
                var access = await blobs
                    .CreatePresignedGetAsync(record.Storage.Bucket, record.Storage.Key, expiry, cancellationToken)
                    .ConfigureAwait(false);
                viewUrl = access.Url;
                expiresAt = access.ExpiresAt;
            }
            catch
            {
                // optional
            }
        }

        return new ToolResult
        {
            IsSuccess = true,
            Output = VisualToolParams.ToJson(new { asset = record, viewUrl, viewUrlExpiresAt = expiresAt })
        };
    }

    private static async Task<ToolResult> AnnotateAsync(
        IDictionary<string, object> p,
        CancellationToken cancellationToken)
    {
        var catalog = ProjectMemoryServiceAccessor.GetRequiredService<VisualAssetCatalogStore>();
        var root = VisualToolParams.ResolveProjectRoot(p);
        var scenarioId = VisualToolParams.RequireScenarioId(p);
        var assetId = VisualToolParams.RequireAssetId(p);

        var record = await catalog.LoadAsync(root, scenarioId, assetId, cancellationToken).ConfigureAwait(false);
        if (record == null)
            return new ToolResult { IsSuccess = false, Error = "asset_not_found" };

        var subjects = VisualToolParams.ParseSubjects(p);
        if (subjects is { Count: > 0 })
            record.Subjects = subjects;

        var caption = VisualToolParams.GetString(p, "userCaption");
        if (caption != null)
            record.Context.UserCaption = caption;

        var sensitivity = VisualToolParams.GetString(p, "sensitivity");
        if (!string.IsNullOrWhiteSpace(sensitivity))
            record.Privacy.Sensitivity = sensitivity.Trim();

        if (record.Subjects.Count > 0 && string.Equals(record.State, VisualAssetStates.Uploaded, StringComparison.OrdinalIgnoreCase))
            record.State = VisualAssetStates.Ready;

        await catalog.SaveAsync(root, scenarioId, record, cancellationToken).ConfigureAwait(false);
        return new ToolResult { IsSuccess = true, Output = VisualToolParams.ToJson(record) };
    }

    private static async Task<ToolResult> InferFromPromptAsync(
        IDictionary<string, object> p,
        CancellationToken cancellationToken)
    {
        var pipeline = ProjectMemoryServiceAccessor.GetRequiredService<IVisualPipelineService>();
        var root = VisualToolParams.ResolveProjectRoot(p);
        var scenarioId = VisualToolParams.RequireScenarioId(p);
        var assetId = VisualToolParams.RequireAssetId(p);

        var result = await pipeline.InferFromPromptAsync(new VisualInferRequest
        {
            ProjectRoot = root,
            ScenarioId = scenarioId,
            AssetId = assetId,
            UserMessage = VisualToolParams.GetString(p, "userMessage"),
            FocusEntityKey = VisualToolParams.GetString(p, "focusEntityKey")
        }, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            return new ToolResult { IsSuccess = false, Error = result.Error ?? "infer_failed" };

        return new ToolResult { IsSuccess = true, Output = VisualToolParams.ToJson(result.Record) };
    }

    private static async Task<ToolResult> LinkToTurnAsync(
        IDictionary<string, object> p,
        CancellationToken cancellationToken)
    {
        var catalog = ProjectMemoryServiceAccessor.GetRequiredService<VisualAssetCatalogStore>();
        var root = VisualToolParams.ResolveProjectRoot(p);
        var scenarioId = VisualToolParams.RequireScenarioId(p);
        var assetId = VisualToolParams.RequireAssetId(p);
        var sessionId = VisualToolParams.GetString(p, "sessionId");
        var turnGroupId = VisualToolParams.GetString(p, "turnGroupId");

        var record = await catalog.LoadAsync(root, scenarioId, assetId, cancellationToken).ConfigureAwait(false);
        if (record == null)
            return new ToolResult { IsSuccess = false, Error = "asset_not_found" };

        if (!string.IsNullOrWhiteSpace(sessionId))
            record.UploadedBySessionId = sessionId.Trim();
        if (!string.IsNullOrWhiteSpace(turnGroupId))
            record.SourceTurnGroupId = turnGroupId.Trim();

        await catalog.SaveAsync(root, scenarioId, record, cancellationToken).ConfigureAwait(false);
        return new ToolResult { IsSuccess = true, Output = VisualToolParams.ToJson(record) };
    }

    private static async Task<ToolResult> DeleteAssetAsync(
        IDictionary<string, object> p,
        CancellationToken cancellationToken)
    {
        var deleter = ProjectMemoryServiceAccessor.GetRequiredService<VisualAssetDeleter>();
        var root = VisualToolParams.ResolveProjectRoot(p);
        var scenarioId = VisualToolParams.RequireScenarioId(p);
        var assetId = VisualToolParams.RequireAssetId(p);

        var result = await deleter
            .DeleteAsync(root, scenarioId, assetId, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
            return new ToolResult { IsSuccess = false, Error = result.Error ?? "delete_failed" };

        return new ToolResult
        {
            IsSuccess = true,
            Output = VisualToolParams.ToJson(new
            {
                assetId = result.AssetId,
                deleted = true,
                blobDeleted = result.BlobDeleted,
                alreadyDeleted = result.AlreadyDeleted
            })
        };
    }
}
