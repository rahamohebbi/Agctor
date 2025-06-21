using System;
using System.Linq;
using System.Reflection;

namespace AgctorSDK.CodeGraph.Snippets
{
    /// <summary>
    /// Scans an assembly for implementations of <see cref="ISnippetProvider"/> and registers them.
    /// Only providers with a public or internal parameter-less constructor are instantiated.
    /// </summary>
    public static class SnippetProviderBootstrapper
    {
        public static void RegisterProvidersFromAssembly(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes()
                                          .Where(t => !t.IsAbstract && typeof(ISnippetProvider).IsAssignableFrom(t)))
            {
                if (SnippetProviderRegistry.IsRegistered(type)) continue;

                // Try create instance with parameterless ctor (non-public allowed)
                try
                {
                    var provider = Activator.CreateInstance(type, nonPublic: true) as ISnippetProvider;
                    if (provider != null)
                    {
                        SnippetProviderRegistry.Register(provider);
                    }
                }
                catch
                {
                    // Ignore types that cannot be constructed (e.g., require DI)
                }
            }
        }

        /// <summary>
        /// Registers providers from the CodeGraph assembly (built-ins).
        /// Call this once during startup.
        /// </summary>
        public static void RegisterBuiltIn()
        {
            RegisterProvidersFromAssembly(typeof(SnippetProviderBootstrapper).Assembly);
        }
    }
} 