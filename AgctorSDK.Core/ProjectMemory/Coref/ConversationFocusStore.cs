using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>
/// File-backed implementation. One <c>focus-&lt;scenario&gt;.yaml</c> per scenario under
/// <c>.agctor/runtime/coref/</c>. Per-file lock guards read-modify-write across concurrent
/// requests; the directory is created lazily on first save.
/// </summary>
public sealed class ConversationFocusStore : IConversationFocusStore
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new();

    private static object LockFor(string path) => FileLocks.GetOrAdd(path, _ => new object());

    private static string SanitizeScenario(string? scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId)) return "_default";
        return PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
    }

    public Task<ConversationFocus?> LoadAsync(string projectRoot, string? scenarioId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return Task.FromResult<ConversationFocus?>(null);

        var seg = SanitizeScenario(scenarioId);
        var path = ConversationFocusPaths.FocusFile(projectRoot, seg);
        if (!File.Exists(path))
            return Task.FromResult<ConversationFocus?>(null);

        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return Task.FromResult<ConversationFocus?>(null);

            try
            {
                var focus = ProjectYamlSerializer.Deserialize<ConversationFocus>(text);
                if (string.IsNullOrWhiteSpace(focus?.EntityKey)
                    || FocusEntityPolicy.IsPlaceholderSlug(focus.EntityKey))
                    return Task.FromResult<ConversationFocus?>(null);
                return Task.FromResult<ConversationFocus?>(focus);
            }
            catch
            {
                // Corrupt YAML should not break the pipeline; treat as no-focus.
                return Task.FromResult<ConversationFocus?>(null);
            }
        }
    }

    public Task SaveAsync(string projectRoot, string? scenarioId, ConversationFocus focus, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || focus == null || string.IsNullOrWhiteSpace(focus.EntityKey))
            return Task.CompletedTask;

        if (FocusEntityPolicy.IsPlaceholderSlug(focus.EntityKey))
            return Task.CompletedTask;

        var seg = SanitizeScenario(scenarioId);
        var dir = ConversationFocusPaths.Directory(projectRoot);
        Directory.CreateDirectory(dir);
        var path = ConversationFocusPaths.FocusFile(projectRoot, seg);

        if (string.IsNullOrWhiteSpace(focus.UpdatedAtUtc))
            focus.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(path, ProjectYamlSerializer.Serialize(focus));
        }

        return Task.CompletedTask;
    }
}
