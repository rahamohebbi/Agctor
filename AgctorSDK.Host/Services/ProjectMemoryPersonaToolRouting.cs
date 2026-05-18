namespace AgctorSDK.Host.Services;

/// <summary>
/// Maps project-memory YAML persona ids to host <see cref="AgctorSDK.Core.Tools.IToolActor"/> CLR names.
/// Used by the Tools dashboard when <c>tools.allow</c> lists semantic ops (read_document) but not HTTP tool ids.
/// </summary>
public static class ProjectMemoryPersonaToolRouting
{
    public static IReadOnlyList<(string PersonaId, string ClrToolName)> KnownRoutes { get; } =
        new List<(string, string)>
        {
            ("person-query", "PersonMemoryContextTool"),
            ("memory-curator", "ApplyMemoryIntentsTool")
        };
}
