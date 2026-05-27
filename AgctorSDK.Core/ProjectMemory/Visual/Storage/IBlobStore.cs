using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Visual.Storage;

/// <summary>S3-compatible object storage for visual asset blobs (PRD-023).</summary>
public interface IBlobStore
{
    Task<PresignedBlobUpload> CreatePresignedUploadAsync(
        string bucket,
        string key,
        string contentType,
        long maxBytes,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    Task<PresignedBlobAccess> CreatePresignedGetAsync(
        string bucket,
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    Task<bool> ObjectExistsAsync(string bucket, string key, CancellationToken cancellationToken = default);

    /// <summary>Verifies object exists and matches <paramref name="expectedSha256Hex"/> when provided.</summary>
    Task VerifyUploadedAsync(
        string bucket,
        string key,
        string? expectedSha256Hex,
        CancellationToken cancellationToken = default);

    Task DeleteObjectAsync(string bucket, string key, CancellationToken cancellationToken = default);

    /// <summary>Reads the full object bytes (vision extract / infer download).</summary>
    Task<byte[]> ReadObjectBytesAsync(string bucket, string key, CancellationToken cancellationToken = default);
}

public sealed class PresignedBlobUpload
{
    public required string UploadUrl { get; init; }

    public IReadOnlyDictionary<string, string> UploadHeaders { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class PresignedBlobAccess
{
    public required string Url { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}
