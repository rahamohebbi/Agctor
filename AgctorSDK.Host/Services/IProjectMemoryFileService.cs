namespace AgctorSDK.Host.Services;

public interface IProjectMemoryFileService
{
    Task<string> ReadAsync(string projectRoot, string relativePath, CancellationToken cancellationToken = default);
    Task WriteAsync(string projectRoot, string relativePath, string content, CancellationToken cancellationToken = default);
    Task DeleteAsync(string projectRoot, string relativePath, CancellationToken cancellationToken = default);
    bool FileExists(string projectRoot, string relativePath);
}
