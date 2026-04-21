using System;
using System.IO;
using AgctorSDK.Core.ProjectMemory.Resolution.Messages;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Persistence;

/// <summary>
/// Persists <see cref="SessionSummary"/> checkpoints under <c>&lt;projectRoot&gt;/sessions/&lt;sessionId&gt;/summary.yaml</c>.
/// Split out from the reconciler so unit tests can verify disk state without spinning actors.
/// </summary>
public sealed class SessionSummaryStore
{
    private readonly string _projectRoot;

    public SessionSummaryStore(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("project root required", nameof(projectRoot));
        _projectRoot = projectRoot;
    }

    public string PathFor(string sessionId) =>
        Path.Combine(_projectRoot, "sessions", Sanitize(sessionId), "summary.yaml");

    public void Save(SessionSummary summary)
    {
        if (summary == null) throw new ArgumentNullException(nameof(summary));
        var path = PathFor(summary.SessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ProjectYamlSerializer.Serialize(summary));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    public SessionSummary? Load(string sessionId)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        return string.IsNullOrWhiteSpace(text) ? null : ProjectYamlSerializer.Deserialize<SessionSummary>(text);
    }

    private static string Sanitize(string id) => string.IsNullOrWhiteSpace(id) ? "_unknown" : id.Replace('/', '_').Replace('\\', '_');
}
