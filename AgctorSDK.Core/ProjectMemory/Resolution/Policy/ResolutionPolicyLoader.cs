using System.IO;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Policy;

/// <summary>
/// Loads <c>&lt;projectRoot&gt;/.agctor/resolution.yaml</c> into a <see cref="ResolutionPolicy"/>.
/// Missing file means "use defaults, feature disabled"; an empty file means "defaults with enabled
/// unchanged from default (false)". Extra keys are ignored.
/// </summary>
public static class ResolutionPolicyLoader
{
    public static ResolutionPolicy Load(string projectRoot)
    {
        var path = ResolutionPaths.PolicyPath(projectRoot);
        if (!File.Exists(path))
            return ResolutionPolicy.CreateDefault();

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return ResolutionPolicy.CreateDefault();

        var loaded = ProjectYamlSerializer.Deserialize<ResolutionPolicy>(text);
        return loaded ?? ResolutionPolicy.CreateDefault();
    }
}
