using System.Collections.Concurrent;
using System.Text.Json;

namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>JSON file-backed PRD-024 runtime snapshot store.</summary>
public sealed class ScenarioFlowRuntimeStore : IScenarioFlowRuntimeStore
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static object LockFor(string path) => FileLocks.GetOrAdd(path, _ => new object());

    public Task<ScenarioFlowRuntimeSnapshot?> LoadAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        var path = ScenarioFlowRuntimePaths.SnapshotFile(projectRoot, sessionId, scenarioId);
        if (!File.Exists(path))
            return Task.FromResult<ScenarioFlowRuntimeSnapshot?>(null);

        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return Task.FromResult<ScenarioFlowRuntimeSnapshot?>(null);

            try
            {
                var snap = JsonSerializer.Deserialize<ScenarioFlowRuntimeSnapshot>(text, JsonOptions);
                return Task.FromResult(snap);
            }
            catch
            {
                return Task.FromResult<ScenarioFlowRuntimeSnapshot?>(null);
            }
        }
    }

    public Task SaveAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        ScenarioFlowRuntimeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var path = ScenarioFlowRuntimePaths.SnapshotFile(projectRoot, sessionId, scenarioId);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        snapshot.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);

        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(path, json);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string projectRoot,
        string sessionId,
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        var path = ScenarioFlowRuntimePaths.SnapshotFile(projectRoot, sessionId, scenarioId);
        if (!File.Exists(path))
            return Task.CompletedTask;

        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
