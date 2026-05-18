using System.Reflection;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Implementations;

namespace AgctorSDK.Extensions.Services;

/// <summary>
/// Registry of <see cref="IToolActor"/> types, HTTP ids, and discovery metadata.
/// Populated via <see cref="CreateFromAssemblies"/> / <see cref="AgctorHostToolAttribute"/> scan.
/// </summary>
public sealed class AgctorToolCatalog
{
    public sealed record ToolCatalogEntry(
        string PrimaryId,
        string ClrTypeName,
        Type ActorType,
        ToolInfo Discovery,
        bool ExposeOnHttpApi,
        string DefaultOperation = "");

    private readonly List<ToolCatalogEntry> _entries;
    private readonly Dictionary<string, ToolCatalogEntry> _resolveHttp = new(StringComparer.OrdinalIgnoreCase);

    private AgctorToolCatalog(List<ToolCatalogEntry> entries)
    {
        _entries = entries;
        RebuildHttpIndex();
    }

    public static AgctorToolCatalog CreateFromAssemblies(params Assembly[] assemblies) =>
        new(ToolActorDiscovery.ScanAssemblies(assemblies).ToList());

    public static AgctorToolCatalog CreateDefault() =>
        CreateFromAssemblies(typeof(FileSystemTool).Assembly);

    public void RegisterEntry(ToolCatalogEntry entry, IAgentFactory? factory = null)
    {
        if (_entries.Any(e => string.Equals(e.ClrTypeName, entry.ClrTypeName, StringComparison.OrdinalIgnoreCase)))
            return;
        _entries.Add(entry);
        RebuildHttpIndex();
        if (factory != null)
            RegisterOne(factory, entry.ActorType);
    }

    private void RebuildHttpIndex()
    {
        _resolveHttp.Clear();
        foreach (var e in _entries.Where(x => x.ExposeOnHttpApi))
        {
            _resolveHttp[e.PrimaryId] = e;
            _resolveHttp[e.ClrTypeName] = e;
        }
    }

    public void RegisterToolActorTypes(IAgentFactory factory)
    {
        foreach (var actorType in _entries.Select(e => e.ActorType).Distinct())
            RegisterOne(factory, actorType);
    }

    private static void RegisterOne(IAgentFactory factory, Type actorType)
    {
        var open = typeof(AgctorToolCatalog).GetMethod(nameof(RegisterGeneric), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("AgctorToolCatalog.RegisterGeneric is missing.");
        open.MakeGenericMethod(actorType).Invoke(null, new object[] { factory });
    }

    private static void RegisterGeneric<T>(IAgentFactory factory) where T : class, IActor, IToolActor =>
        factory.RegisterToolActorType<T>();

    public bool TryGetHttpEntry(string toolIdOrAlias, out ToolCatalogEntry entry) =>
        _resolveHttp.TryGetValue(toolIdOrAlias, out entry!);

    public IReadOnlyList<string> GetHttpToolPrimaryIds() =>
        _entries.Where(e => e.ExposeOnHttpApi).Select(e => e.PrimaryId).ToList();

    public IReadOnlyList<ToolCatalogEntry> GetAllEntries() => _entries;
}
