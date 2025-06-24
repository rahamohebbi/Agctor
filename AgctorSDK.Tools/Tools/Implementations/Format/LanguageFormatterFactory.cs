using System.Collections.Concurrent;
using AgctorSDK.Core.Tools.Abstractions;
using System;

namespace AgctorSDK.Core.Tools.Implementations.Format
{
    /// <summary>
    /// Resolves <see cref="ICodeFormatter"/> instances for a given language, maintaining a per-process cache
    /// of formatter availability checks.
    /// </summary>
    internal static class LanguageFormatterFactory
    {
        private static readonly ConcurrentDictionary<string, ICodeFormatter> _formatters = new(StringComparer.OrdinalIgnoreCase);

        static LanguageFormatterFactory()
        {
            // Register built-in formatters here. New languages can be appended without touching the caller code.
            Register(new CSharpFormatter());
            Register(new PythonFormatter());
        }

        private static void Register(ICodeFormatter formatter)
        {
            _formatters[formatter.Language] = formatter;
        }

        public static bool TryGet(string language, out ICodeFormatter formatter) => _formatters.TryGetValue(language, out formatter!);
    }
} 