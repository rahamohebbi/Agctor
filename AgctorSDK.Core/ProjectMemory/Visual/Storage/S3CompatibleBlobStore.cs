using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Cryptography;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.ProjectMemory.Visual.Storage;

/// <summary>MinIO / AWS / R2 via AWSSDK with path-style addressing.</summary>
public sealed class S3CompatibleBlobStore : IBlobStore
{
    private readonly VisualStorageOptions _options;
    private readonly ILogger<S3CompatibleBlobStore> _logger;

    public S3CompatibleBlobStore(IOptions<VisualStorageOptions> options, ILogger<S3CompatibleBlobStore> logger)
    {
        _options = options?.Value ?? new VisualStorageOptions();
        _logger = logger;
    }

    public async Task<PresignedBlobUpload> CreatePresignedUploadAsync(
        string bucket,
        string key,
        string contentType,
        long maxBytes,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureBucketAsync(bucket, cancellationToken).ConfigureAwait(false);

        using var client = CreateClient();
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(expiry),
            ContentType = contentType
        };

        var url = client.GetPreSignedURL(request);
        return new PresignedBlobUpload
        {
            UploadUrl = url,
            UploadHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = contentType
            },
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiry)
        };
    }

    public Task<PresignedBlobAccess> CreatePresignedGetAsync(
        string bucket,
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = CreateClient();
        var url = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry)
        });

        return Task.FromResult(new PresignedBlobAccess
        {
            Url = url,
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiry)
        });
    }

    public async Task<bool> ObjectExistsAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = CreateClient();
        try
        {
            await client.GetObjectMetadataAsync(bucket, key, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task VerifyUploadedAsync(
        string bucket,
        string key,
        string? expectedSha256Hex,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = CreateClient();

        if (!await ObjectExistsAsync(bucket, key, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"Visual object s3://{bucket}/{key} was not found after upload.");

        if (string.IsNullOrWhiteSpace(expectedSha256Hex))
            return;

        using var response = await client.GetObjectAsync(bucket, key, cancellationToken).ConfigureAwait(false);
        if (response.ContentLength > _options.MaxUploadBytes)
            throw new InvalidOperationException($"Visual object exceeds max size ({_options.MaxUploadBytes} bytes).");

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(response.ResponseStream, cancellationToken).ConfigureAwait(false);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        if (!hex.Equals(expectedSha256Hex.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SHA-256 mismatch for uploaded visual object.");
    }

    public async Task DeleteObjectAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = CreateClient();
        await client.DeleteObjectAsync(bucket, key, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadObjectBytesAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = CreateClient();
        using var response = await client.GetObjectAsync(bucket, key, cancellationToken).ConfigureAwait(false);
        if (response.ContentLength > _options.MaxUploadBytes)
            throw new InvalidOperationException($"Visual object exceeds max size ({_options.MaxUploadBytes} bytes).");

        await using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }

    private AmazonS3Client CreateClient()
    {
        var cfg = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint.TrimEnd('/'),
            ForcePathStyle = _options.ForcePathStyle,
            AuthenticationRegion = _options.Region
        };

        if (!string.IsNullOrWhiteSpace(_options.AccessKey) && !string.IsNullOrWhiteSpace(_options.SecretKey))
            return new AmazonS3Client(_options.AccessKey, _options.SecretKey, cfg);

        // MinIO default dev credentials
        return new AmazonS3Client("minioadmin", "minioadmin", cfg);
    }

    private async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        try
        {
            await client.PutBucketAsync(bucket, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogDebug(ex, "PutBucket {Bucket} skipped (may already exist)", bucket);
        }
    }
}
