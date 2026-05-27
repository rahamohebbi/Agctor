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
    public static string BuildExtractPrompt(AgentDefinitionSpec spec, string userMessage, string? conversationPrefix) =>
        BuildExtractPrompt(spec, userMessage, conversationPrefix, activeSubjectEntityKey: null, activeSubjectDisplayName: null);

    /// <summary>
    /// Overload that includes the persistent <em>active subject</em> hint (PRD-019 Option F). When supplied,
    /// the extractor must default <c>entityKey</c> to this slug for any reference that does not name a person
    /// explicitly, and must never fabricate an entityKey from the scenario folder name.
    /// </summary>
    public static string BuildExtractPrompt(
        AgentDefinitionSpec spec,
        string userMessage,
        string? conversationPrefix,
        string? activeSubjectEntityKey,
        string? activeSubjectDisplayName)
    {
        var lines = (spec.Instructions ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i));
        var sys = string.Join('\n', lines)
                  + "\n\nRespond with ONLY valid JSON: {\"memoryIntents\":[{\"entityKey\":\"\",\"knowledgeType\":\"\",\"attribute\":\"\",\"value\":\"\",\"confidence\":0.9}]}\n";

        if (!string.IsNullOrWhiteSpace(activeSubjectEntityKey))
        {
            var sb = new StringBuilder(sys);
            AppendActiveSubjectHint(sb, activeSubjectEntityKey, activeSubjectDisplayName);
            sys = sb.ToString();
        }

        if (!string.IsNullOrWhiteSpace(conversationPrefix))
            sys += "\nPrior conversation:\n" + conversationPrefix.Trim() + "\n---\n";
        return sys + "\nInput:\n" + userMessage;
    }

    /// <summary>Shared active-subject block for extractor prompts (playground + pipeline).</summary>
    public static void AppendActiveSubjectHint(
        StringBuilder sb,
        string? activeSubjectEntityKey,
        string? activeSubjectDisplayName)
    {
        if (string.IsNullOrWhiteSpace(activeSubjectEntityKey))
            return;

        var key = activeSubjectEntityKey.Trim();
        var displayBlock = string.IsNullOrWhiteSpace(activeSubjectDisplayName)
            ? key
            : activeSubjectDisplayName.Trim() + " (" + key + ")";
        sb.Append("\n\nActive subject for this scenario: ").Append(displayBlock)
            .Append("\nWhen the user does not name a person explicitly (any language), set entityKey to '")
            .Append(key)
            .Append("'.")
            .Append("\nNever use placeholders such as user, unknown, or the scenario id as entityKey.")
            .Append("\nFirst-person references (I, me, my) refer to this active subject.\n");
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
        return instructions + "\nContext:\n" + entityContext
               + "\nUse stored context together with factual statements in the Question below."
               + "\n" + prefix + "Question:\n" + userMessage;
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

