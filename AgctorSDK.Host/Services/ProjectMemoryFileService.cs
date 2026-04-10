namespace AgctorSDK.Host.Services;

public sealed class ProjectMemoryFileService : IProjectMemoryFileService
{
    public Task<string> ReadAsync(string projectRoot, string relativePath, CancellationToken cancellationToken = default)
    {
        var full = ProjectMemoryPathSecurity.GetSafeFullPath(projectRoot, relativePath);
        if (!File.Exists(full))
            throw new FileNotFoundException(full);
        return File.ReadAllTextAsync(full, cancellationToken);
    }

    public async Task WriteAsync(string projectRoot, string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var full = ProjectMemoryPathSecurity.GetSafeFullPath(projectRoot, relativePath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(full, content, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string projectRoot, string relativePath, CancellationToken cancellationToken = default)
    {
        var full = ProjectMemoryPathSecurity.GetSafeFullPath(projectRoot, relativePath);
        if (File.Exists(full))
            File.Delete(full);
        return Task.CompletedTask;
    }

    public bool FileExists(string projectRoot, string relativePath)
    {
        try
        {
            var full = ProjectMemoryPathSecurity.GetSafeFullPath(projectRoot, relativePath);
            return File.Exists(full);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
