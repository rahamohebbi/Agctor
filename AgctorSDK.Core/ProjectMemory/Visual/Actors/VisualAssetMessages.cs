using System;
using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.Visual.Models;

namespace AgctorSDK.Core.ProjectMemory.Visual.Actors;

public sealed record VisualAssetInitUploadRequest(
    string ProjectRoot,
    string ScenarioId,
    string ContentType,
    long Bytes,
    string? SessionId,
    string? TurnGroupId);

public sealed record VisualAssetInitUploadResult(
    bool Success,
    string? AssetId,
    string? UploadUrl,
    IReadOnlyDictionary<string, string>? UploadHeaders,
    DateTimeOffset? ExpiresAt,
    string? Error);

public sealed record VisualAssetCompleteUploadRequest(
    string ProjectRoot,
    string ScenarioId,
    string AssetId,
    string? Sha256Hex);

public sealed record VisualAssetCompleteUploadResult(
    bool Success,
    VisualAssetRecord? Asset,
    string? Error);
