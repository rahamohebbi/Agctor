namespace AgctorSDK.Host.Services;

/// <summary>
/// Static hints for which <see cref="AgctorSDK.Core.Agents.IAgent"/> C# types are known to invoke which
/// <see cref="AgctorSDK.Core.Tools.IToolActor"/> keys. Extend when new agents hard-code tool routing.
/// </summary>
public static class CSharpAgentToolAffinities
{
    /// <summary>Agent type name → CLR tool type names passed to <see cref="AgctorSDK.Core.Interfaces.IAgentFactory.InvokeToolByPromptAsync"/> or subtasks.</summary>
    public static IReadOnlyDictionary<string, string[]> KnownToolKeysByAgentType { get; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["LLMAgent"] = new[] { "CodeEditorTool" },
            ["CoderAgent"] = new[] { "CodeEditorTool", "CompileTool", "TestRunnerTool" },
            // Base Agent routes any registered IToolActor by name from subtasks; list is open-ended at runtime.
            ["Agent"] = Array.Empty<string>()
        };
}
