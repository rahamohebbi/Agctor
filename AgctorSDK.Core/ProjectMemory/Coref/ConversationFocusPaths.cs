using System.IO;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>Paths for the conversation focus state (PRD-019 Option F: persistent active subject).</summary>
public static class ConversationFocusPaths
{
    /// <summary><c>{projectRoot}/.agctor/runtime/coref/</c></summary>
    public static string Directory(string projectRoot) =>
        Path.GetFullPath(Path.Combine(projectRoot.Trim(), ".agctor", "runtime", "coref"));

    /// <summary>One YAML file per scenario so a brand-new browser session inherits the prior active subject.</summary>
    public static string FocusFile(string projectRoot, string scenarioSegment)
    {
        var safeSegment = string.IsNullOrWhiteSpace(scenarioSegment) ? "_default" : scenarioSegment.Trim();
        return Path.Combine(Directory(projectRoot), "focus-" + safeSegment + ".yaml");
    }
}
