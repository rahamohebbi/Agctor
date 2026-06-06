using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.ProjectMemory.Visual.Storage;

/// <summary>Local blob store under <c>{projectRoot}/.agctor/visual-blobs/</c> for dev/tests without MinIO.</summary>
public sealed class FileSystemBlobStore : IBlobStore
{
    private readonly VisualStorageOptions _options;
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions>? _projectOptions;

    public FileSystemBlobStore(IOptions<VisualStorageOptions> options)
        : this(options, projectOptions: null)
    {
    }

    public FileSystemBlobStore(
        IOptions<VisualStorageOptions> options,
        IOptionsMonitor<ProjectMemoryAgentOptions>? projectOptions)
    {
        _options = options?.Value ?? new VisualStorageOptions();
        _projectOptions = projectOptions;
    }

    public Task<PresignedBlobUpload> CreatePresignedUploadAsync(
        string bucket,
        string key,
        string contentType,
        long maxBytes,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(bucket, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // File provider: client PUTs to API complete with body in 023b; for 023a return file:// hint via custom header.
        var expires = DateTimeOffset.UtcNow.Add(expiry);
        return Task.FromResult(new PresignedBlobUpload
        {
            UploadUrl = $"file://{path.Replace('\\', '/')}",
            UploadHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = contentType,
                ["X-Agctor-Visual-Provider"] = "file",
                ["X-Agctor-Max-Bytes"] = maxBytes.ToString()
            },
            ExpiresAt = expires
        });
    }

    public Task<PresignedBlobAccess> CreatePresignedGetAsync(
        string bucket,
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(bucket, key);
        if (!File.Exists(path))
            throw new FileNotFoundException("Visual blob not found.", path);

        return Task.FromResult(new PresignedBlobAccess
        {
            Url = $"file://{path.Replace('\\', '/')}",
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiry)
        });
    }

    public Task<bool> ObjectExistsAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(ResolvePath(bucket, key)));
    }

    public Task VerifyUploadedAsync(
        string bucket,
        string key,
        string? expectedSha256Hex,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(bucket, key);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Visual blob missing at '{path}'.");

        var info = new FileInfo(path);
        if (info.Length > _options.MaxUploadBytes)
            throw new InvalidOperationException($"Visual blob exceeds max size ({_options.MaxUploadBytes} bytes).");

        if (!string.IsNullOrWhiteSpace(expectedSha256Hex))
        {
            var hash = ComputeSha256Hex(path);
            if (!hash.Equals(expectedSha256Hex.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SHA-256 mismatch for uploaded visual blob.");
        }

        return Task.CompletedTask;
    }

    public Task DeleteObjectAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(bucket, key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<byte[]> ReadObjectBytesAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(bucket, key);
        if (!File.Exists(path))
            throw new FileNotFoundException("Visual blob not found.", path);
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes bytes for file-provider uploads (Host calls on complete).</summary>
    public async Task WriteObjectAsync(
        string bucket,
        string key,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(bucket, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
    }

    private string ResolvePath(string bucket, string key)
    {
        var safeBucket = bucket.Replace('\\', '_').Replace('/', '_');
        var safeKey = key.Replace('\\', '/').TrimStart('/');
        var relKey = safeKey.Replace('/', Path.DirectorySeparatorChar);

        var projectRoot = _projectOptions?.CurrentValue?.ProjectRoot?.Trim();
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var rootFull = Path.GetFullPath(projectRoot);
            if (Directory.Exists(Path.Combine(rootFull, ".agctor")))
            {
                return Path.GetFullPath(Path.Combine(rootFull, ".agctor", "visual-blobs", safeBucket, relKey));
            }
        }

        // Fallback when no project root is configured (unit tests, ephemeral runs).
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "visual-blobs", safeBucket, relKey));
    }

    private static string ComputeSha256Hex(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
