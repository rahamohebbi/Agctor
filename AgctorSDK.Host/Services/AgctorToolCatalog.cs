using System.Reflection;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Implementations;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Single place for: which <see cref="IToolActor"/> types exist in the host, their stable HTTP ids,
/// and discovery metadata. <see cref="RegisterToolActorTypes"/> mirrors this list onto <see cref="IAgentFactory"/>.
/// </summary>
public sealed class AgctorToolCatalog
{
    /// <param name="ClrTypeName">Key passed to <see cref="IAgentFactory.InvokeToolRequestAsync"/> (defaults to CLR type name).</param>
    public sealed record ToolCatalogEntry(string PrimaryId, string ClrTypeName, Type ActorType, ToolInfo Discovery, bool ExposeOnHttpApi);

    private readonly List<ToolCatalogEntry> _entries;
    private readonly Dictionary<string, ToolCatalogEntry> _resolveHttp = new(StringComparer.OrdinalIgnoreCase);

    private AgctorToolCatalog(List<ToolCatalogEntry> entries)
    {
        _entries = entries;
        foreach (var e in entries.Where(x => x.ExposeOnHttpApi))
        {
            _resolveHttp[e.PrimaryId] = e;
            _resolveHttp[e.ClrTypeName] = e;
        }
    }

    /// <summary>Builds the default host catalog (all tool actors + HTTP surface for the three legacy tool ids).</summary>
    public static AgctorToolCatalog CreateDefault()
    {
        var list = new List<ToolCatalogEntry>
        {
            new(
                "file-system",
                nameof(FileSystemTool),
                typeof(FileSystemTool),
                new ToolInfo
                {
                    Id = "file-system",
                    Name = "File System Tool",
                    Description = "Performs file system operations like read, write, list directories",
                    Parameters = new Dictionary<string, object>
                    {
                        ["operation"] = "string: read, write, list, delete",
                        ["path"] = "string: file or directory path",
                        ["content"] = "string: content for write operations (optional)"
                    }
                },
                ExposeOnHttpApi: true),
            new(
                "code-executor",
                nameof(CodeExecutorTool),
                typeof(CodeExecutorTool),
                new ToolInfo
                {
                    Id = "code-executor",
                    Name = "Code Executor Tool",
                    Description = "Executes code in various languages (Python, C#, etc.)",
                    Parameters = new Dictionary<string, object>
                    {
                        ["language"] = "string: python, csharp, javascript",
                        ["code"] = "string: code to execute",
                        ["timeout"] = "int: execution timeout in seconds (optional)"
                    }
                },
                ExposeOnHttpApi: true),
            new(
                "code-editor",
                nameof(CodeEditorTool),
                typeof(CodeEditorTool),
                new ToolInfo
                {
                    Id = "code-editor",
                    Name = "Code Editor Tool",
                    Description = "Edits and manipulates code files",
                    Parameters = new Dictionary<string, object>
                    {
                        ["operation"] = "string: edit, format, analyze",
                        ["file"] = "string: file path",
                        ["changes"] = "object: changes to apply (optional)"
                    }
                },
                ExposeOnHttpApi: true),
            new(
                "compile",
                nameof(CompileTool),
                typeof(CompileTool),
                new ToolInfo
                {
                    Id = "compile",
                    Name = "Compile Tool",
                    Description = "Builds projects or solutions",
                    Parameters = new Dictionary<string, object>()
                },
                ExposeOnHttpApi: false),
            new(
                "test-runner",
                nameof(TestRunnerTool),
                typeof(TestRunnerTool),
                new ToolInfo
                {
                    Id = "test-runner",
                    Name = "Test Runner Tool",
                    Description = "Runs unit tests",
                    Parameters = new Dictionary<string, object>()
                },
                ExposeOnHttpApi: false),
            new(
                "format",
                nameof(FormatTool),
                typeof(FormatTool),
                new ToolInfo
                {
                    Id = "format",
                    Name = "Format Tool",
                    Description = "Formats source files",
                    Parameters = new Dictionary<string, object>()
                },
                ExposeOnHttpApi: false),
            new(
                "person-memory-context",
                nameof(PersonMemoryContextTool),
                typeof(PersonMemoryContextTool),
                new ToolInfo
                {
                    Id = "person-memory-context",
                    Name = "Person memory context",
                    Description =
                        "Read-only: loads scenario-scoped people markdown for person-query (BuildContext operation).",
                    Parameters = new Dictionary<string, object>
                    {
                        ["operation"] = "BuildContext",
                        ["projectRoot"] = "string optional — defaults to Agctor:ProjectMemory:ProjectRoot",
                        ["scenarioId"] = "string optional",
                        ["contextStrategy"] = "markdown_all | markdown_focus | …",
                        ["userMessage"] = "string — used for markdown_focus inference",
                        ["agentSpecId"] = "string optional — defaults to person-query (for tools.allow paths)"
                    }
                },
                ExposeOnHttpApi: true),
            new(
                "apply-memory-intents",
                nameof(ApplyMemoryIntentsTool),
                typeof(ApplyMemoryIntentsTool),
                new ToolInfo
                {
                    Id = "apply-memory-intents",
                    Name = "Apply memory intents",
                    Description =
                        "Applies MemoryIntentBatch JSON to markdown files under scenario scope (Apply operation).",
                    Parameters = new Dictionary<string, object>
                    {
                        ["operation"] = "Apply",
                        ["batchJson"] = "string — full MemoryIntentBatch JSON"
                    }
                },
                ExposeOnHttpApi: true)
        };

        return new AgctorToolCatalog(list);
    }

    /// <summary>Registers every distinct actor type once (same set as historical <c>Program.cs</c> block).</summary>
    public void RegisterToolActorTypes(IAgentFactory factory)
    {
        foreach (var actorType in _entries.Select(e => e.ActorType).Distinct())
        {
            RegisterOne(factory, actorType);
        }
    }

    private static void RegisterOne(IAgentFactory factory, Type actorType)
    {
        var open = typeof(AgctorToolCatalog).GetMethod(nameof(RegisterGeneric), BindingFlags.NonPublic | BindingFlags.Static);
        if (open == null)
        {
            throw new InvalidOperationException("AgctorToolCatalog.RegisterGeneric is missing.");
        }

        open.MakeGenericMethod(actorType).Invoke(null, new object[] { factory });
    }

    private static void RegisterGeneric<T>(IAgentFactory factory) where T : class, IActor, IToolActor =>
        factory.RegisterToolActorType<T>();

    public bool TryGetHttpEntry(string toolIdOrAlias, out ToolCatalogEntry entry) =>
        _resolveHttp.TryGetValue(toolIdOrAlias, out entry!);

    public IReadOnlyList<string> GetHttpToolPrimaryIds() =>
        _entries.Where(e => e.ExposeOnHttpApi).Select(e => e.PrimaryId).ToList();

    /// <summary>All catalog entries (HTTP + internal-only tools) for dashboards and insight APIs.</summary>
    public IReadOnlyList<ToolCatalogEntry> GetAllEntries() => _entries;
}
