using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.Tools.LanguageCompilers
{
    /// <summary>
    /// Default implementation of <see cref="ILanguageCompilerFactory"/> maintaining an in-memory registry of compilers.
    /// </summary>
    public class LanguageCompilerFactory : ILanguageCompilerFactory
    {
        private readonly Dictionary<string, ILanguageCompiler> _compilers;
        private readonly Dictionary<string, string> _languageAliases;

        public LanguageCompilerFactory()
        {
            _compilers = new Dictionary<string, ILanguageCompiler>(StringComparer.OrdinalIgnoreCase);
            _languageAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Register built-in compilers
            RegisterCompiler(new CSharpCompiler());

            // Common aliases
            RegisterLanguageAlias("c#", "csharp");
            RegisterLanguageAlias("cs", "csharp");
        }

        public ILanguageCompiler? GetCompiler(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return null;

            if (_languageAliases.TryGetValue(language, out var actual))
            {
                language = actual;
            }

            _compilers.TryGetValue(language, out var compiler);
            return compiler;
        }

        public void RegisterCompiler(ILanguageCompiler compiler)
        {
            if (compiler == null) throw new ArgumentNullException(nameof(compiler));
            _compilers[compiler.Language] = compiler;
        }

        public void RegisterLanguageAlias(string alias, string language)
        {
            if (string.IsNullOrWhiteSpace(alias)) throw new ArgumentNullException(nameof(alias));
            if (string.IsNullOrWhiteSpace(language)) throw new ArgumentNullException(nameof(language));
            _languageAliases[alias] = language;
        }
    }
} 