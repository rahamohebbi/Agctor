using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AgctorSDK.CodeGraph.Analyzers.Abstractions;
using AgctorSDK.CodeGraph.Llm;

namespace AgctorSDK.CodeGraph.Analyzers
{
    /// <summary>
    /// Central registry used by <see cref="FileActor"/> to obtain a language-specific <see cref="ICodeAnalyzer"/>,
    /// and by external callers to register new analyzers at runtime.
    /// </summary>
    public sealed class AnalyzerRegistry
    {
        private readonly ConcurrentDictionary<string, ICodeAnalyzer> _languageMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _extensionToLanguage = new(StringComparer.OrdinalIgnoreCase);
        private ICodeAnalyzer? _fallback; // analyzer registered for "*"

        /// <summary>
        /// Registers the specified <paramref name="analyzer"/> with its declared language and file extensions.
        /// </summary>
        public void RegisterAnalyzer(ICodeAnalyzer analyzer)
        {
            if (analyzer == null) throw new ArgumentNullException(nameof(analyzer));
            _languageMap[analyzer.Language] = analyzer;
            foreach (var ext in analyzer.SupportedFileExtensions)
            {
                if (ext == "*")
                {
                    _fallback = analyzer;
                }
                else
                {
                    var norm = ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
                    _extensionToLanguage[norm] = analyzer.Language;
                }
            }
        }

        /// <summary>
        /// Returns the analyzer registered for <paramref name="language"/> or <c>null</c> if none exists.
        /// </summary>
        public ICodeAnalyzer? GetAnalyzerForLanguage(string language)
        {
            return _languageMap.TryGetValue(language, out var analyzer) ? analyzer : null;
        }

        /// <summary>
        /// Finds an analyzer based on file extension (e.g. ".cs"). Returns <c>null</c> if none exists.
        /// </summary>
        public ICodeAnalyzer? GetAnalyzerForExtension(string extension)
        {
            if (_extensionToLanguage.TryGetValue(extension, out var language))
            {
                return GetAnalyzerForLanguage(language);
            }

            return _fallback;
        }

        /// <summary>
        /// Returns names of all registered languages.
        /// </summary>
        public IReadOnlyCollection<string> RegisteredLanguages => _languageMap.Keys.ToList();

        public void EnableLlmFallback(ILlmClient llmClient)
        {
            var analyzer = new AgctorSDK.CodeGraph.Analyzers.Stubs.LLMAnalyzer(llmClient);
            RegisterAnalyzer(analyzer);
        }
    }
} 