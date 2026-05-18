using System.Reflection;
using AgctorSDK.Core.Tools;

namespace AgctorSDK.Extensions.Services;

/// <summary>Reflects <see cref="AgctorHostToolAttribute"/> on concrete <see cref="IToolActor"/> types.</summary>
public static class ToolActorDiscovery
{
    public static IReadOnlyList<AgctorToolCatalog.ToolCatalogEntry> ScanAssemblies(IEnumerable<Assembly> assemblies)
    {
        var list = new List<AgctorToolCatalog.ToolCatalogEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies)
        {
            if (assembly == null)
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface)
                    continue;
                if (!typeof(IToolActor).IsAssignableFrom(type))
                    continue;

                var attr = type.GetCustomAttribute<AgctorHostToolAttribute>();
                if (attr == null)
                    continue;

                var clrName = type.Name;
                if (!seen.Add(clrName))
                    continue;

                var httpId = attr.HttpId.Trim();
                var discovery = new ToolInfo
                {
                    Id = httpId,
                    Name = attr.DisplayName,
                    Description = attr.Description,
                    Parameters = ToolParameterHints.TryGet(httpId, out var hints)
                        ? hints
                        : BuildDefaultParameters(attr)
                };

                list.Add(new AgctorToolCatalog.ToolCatalogEntry(
                    httpId,
                    clrName,
                    type,
                    discovery,
                    attr.ExposeOnHttpApi,
                    attr.DefaultOperation));
            }
        }

        return list.OrderBy(e => e.ClrTypeName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, object> BuildDefaultParameters(AgctorHostToolAttribute attr)
    {
        var d = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(attr.DefaultOperation))
            d["operation"] = attr.DefaultOperation;
        return d;
    }

    private static class ToolParameterHints
    {
        private static readonly IReadOnlyDictionary<string, Dictionary<string, object>> Map =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["file-system"] = new()
                {
                    ["operation"] = "string: read, write, list, delete",
                    ["path"] = "string: file or directory path",
                    ["content"] = "string: content for write operations (optional)"
                },
                ["code-executor"] = new()
                {
                    ["language"] = "string: python, csharp, javascript",
                    ["code"] = "string: code to execute",
                    ["timeout"] = "int: execution timeout in seconds (optional)"
                },
                ["code-editor"] = new()
                {
                    ["operation"] = "string: edit, format, analyze",
                    ["file"] = "string: file path",
                    ["changes"] = "object: changes to apply (optional)"
                },
                ["person-memory-context"] = new()
                {
                    ["operation"] = "BuildContext",
                    ["projectRoot"] = "string optional — defaults to Agctor:ProjectMemory:ProjectRoot",
                    ["scenarioId"] = "string optional",
                    ["contextStrategy"] = "markdown_all | markdown_focus | …",
                    ["userMessage"] = "string — used for markdown_focus inference",
                    ["agentSpecId"] = "string optional — defaults to person-query"
                },
                ["apply-memory-intents"] = new()
                {
                    ["operation"] = "Apply",
                    ["batchJson"] = "string — full MemoryIntentBatch JSON"
                }
            };

        public static bool TryGet(string httpId, out Dictionary<string, object> hints)
        {
            if (Map.TryGetValue(httpId, out var copy))
            {
                hints = new Dictionary<string, object>(copy);
                return true;
            }

            hints = new Dictionary<string, object>();
            return false;
        }
    }
}
