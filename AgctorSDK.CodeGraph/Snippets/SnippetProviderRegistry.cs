using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.CodeGraph.Snippets
{
    /// <summary>
    /// Simple runtime registry that maps a file to a matching <see cref="ISnippetProvider"/>.
    /// </summary>
    public static class SnippetProviderRegistry
    {
        private static readonly List<ISnippetProvider> _providers = new();
        private static readonly object _sync = new();

        public static void Register(ISnippetProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (_sync)
            {
                _providers.Add(provider);
            }
        }

        public static ISnippetProvider? GetProvider(string filePath)
        {
            lock (_sync)
            {
                return _providers.FirstOrDefault(p => p.CanHandle(filePath));
            }
        }

        internal static bool IsRegistered(Type providerType)
        {
            lock (_sync)
            {
                return _providers.Any(p => p.GetType() == providerType);
            }
        }
    }
} 