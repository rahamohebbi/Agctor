using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.Tools.Implementations.LanguageAdapters
{
    internal static class LanguageAdapterFactory
    {
        private static readonly List<ILanguageAdapter> _adapters = new()
        {
            new CSharpLanguageAdapter()
            // Additional adapters can be registered here.
        };

        public static ILanguageAdapter? GetByExtension(string extLower)
        {
            return _adapters.FirstOrDefault(a => a.Extension.Equals(extLower, StringComparison.OrdinalIgnoreCase));
        }
    }
} 