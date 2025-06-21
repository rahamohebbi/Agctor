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

        public static void Register(ISnippetProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            _providers.Add(provider);
        }

        public static ISnippetProvider? GetProvider(string filePath)
        {
            return _providers.FirstOrDefault(p => p.CanHandle(filePath));
        }

        internal static bool IsRegistered(Type providerType)
            => _providers.Any(p => p.GetType() == providerType);
    }
} 