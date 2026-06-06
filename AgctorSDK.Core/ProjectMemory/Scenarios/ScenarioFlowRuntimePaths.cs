using System.IO;

namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>File paths for PRD-024 runtime snapshots under <c>.agctor/runtime/scenario-flow/</c>.</summary>
public static class ScenarioFlowRuntimePaths
{
    public static string RuntimeDirectory(string projectRoot) =>
        Path.GetFullPath(Path.Combine(projectRoot.Trim(), ".agctor", "runtime", "scenario-flow"));

    public static string SnapshotFile(string projectRoot, string sessionId, string scenarioId)
    {
        var session = SanitizeSegment(sessionId);
        var scenario = SanitizeSegment(scenarioId);
        return Path.Combine(RuntimeDirectory(projectRoot), session, $"{scenario}.json");
    }

    internal static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "_default";

        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetterOrDigit(chars[i]) || chars[i] is '-' or '_')
                continue;
            chars[i] = '_';
        }

        return new string(chars);
    }
}
