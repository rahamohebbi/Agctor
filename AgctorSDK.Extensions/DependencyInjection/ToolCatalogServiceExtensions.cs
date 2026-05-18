using System.Reflection;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Extensions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Extensions.DependencyInjection;

/// <summary>Registers <see cref="AgctorToolCatalog"/> from attributed <see cref="AgctorSDK.Core.Tools.IToolActor"/> types.</summary>
public static class ToolCatalogServiceExtensions
{
    private const string AdditionalAssembliesKey = "Agctor:Tools:AdditionalAssemblies";

    public static IServiceCollection AddAgctorToolCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => BuildCatalog(configuration));
        return services;
    }

    private static AgctorToolCatalog BuildCatalog(IConfiguration configuration)
    {
        var set = new HashSet<Assembly> { typeof(FileSystemTool).Assembly };
        var extra = configuration.GetSection(AdditionalAssembliesKey).Get<string[]>() ?? Array.Empty<string>();
        foreach (var name in extra)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            try
            {
                set.Add(Assembly.Load(new AssemblyName(name.Trim())));
            }
            catch (FileNotFoundException)
            {
                // Optional plugin assembly missing — skip.
            }
        }

        return AgctorToolCatalog.CreateFromAssemblies(set.ToArray());
    }
}
