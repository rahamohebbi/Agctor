using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Shared prompt construction for ProjectMemory workflow actors and the legacy
/// pipeline facade. Keeping these prompts centralized prevents YAML-driven agent
/// behavior from drifting across execution paths.
/// </summary>
public static class ProjectMemoryPromptBuilder
{
    public static string BuildExtractPrompt(AgentDefinitionSpec spec, string userMessage, string? conversationPrefix)
    {
        var lines = (spec.Instructions ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i));
        var sys = string.Join('\n', lines)
                  + "\n\nRespond with ONLY valid JSON: {\"memoryIntents\":[{\"entityKey\":\"\",\"knowledgeType\":\"\",\"attribute\":\"\",\"value\":\"\",\"confidence\":0.9}]}\n";
        if (!string.IsNullOrWhiteSpace(conversationPrefix))
            sys += "\nPrior conversation:\n" + conversationPrefix.Trim() + "\n---\n";
        return sys + "\nInput:\n" + userMessage;
    }

    public static string BuildQueryPrompt(
        AgentDefinitionSpec spec,
        string entityContext,
        string userMessage,
        string? conversationPrefix)
    {
        var instructions = string.Join('\n', spec.Instructions ?? new List<string>());
        var prefix = string.IsNullOrWhiteSpace(conversationPrefix)
            ? ""
            : "Prior conversation:\n" + conversationPrefix.Trim() + "\n---\n";
        return instructions + "\nContext:\n" + entityContext + "\n" + prefix + "Question:\n" + userMessage;
    }

    public static string BuildEntityContext(IEnumerable<(string EntityKey, string Profile)> profiles)
    {
        var sb = new StringBuilder();
        foreach (var (entityKey, profile) in profiles)
        {
            sb.AppendLine($"### {entityKey}");
            sb.AppendLine(profile);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

