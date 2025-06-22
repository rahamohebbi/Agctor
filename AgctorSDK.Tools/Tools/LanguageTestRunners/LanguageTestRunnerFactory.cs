using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Tools.LanguageTestRunners
{
    /// <inheritdoc/>
    public class LanguageTestRunnerFactory : ILanguageTestRunnerFactory
    {
        private readonly Dictionary<string, ILanguageTestRunner> _runners;
        private readonly Dictionary<string, string> _aliases;

        public LanguageTestRunnerFactory()
        {
            _runners = new Dictionary<string, ILanguageTestRunner>(StringComparer.OrdinalIgnoreCase);
            _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Built-ins
            RegisterRunner(new CSharpTestRunner());
            // aliases
            RegisterLanguageAlias("c#", "csharp");
            RegisterLanguageAlias("cs", "csharp");
        }

        public ILanguageTestRunner? GetRunner(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return null;
            if (_aliases.TryGetValue(language, out var actual)) language = actual;
            _runners.TryGetValue(language, out var runner);
            return runner;
        }

        public void RegisterRunner(ILanguageTestRunner runner)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            _runners[runner.Language] = runner;
        }

        public void RegisterLanguageAlias(string alias, string language)
        {
            if (string.IsNullOrWhiteSpace(alias)) throw new ArgumentNullException(nameof(alias));
            if (string.IsNullOrWhiteSpace(language)) throw new ArgumentNullException(nameof(language));
            _aliases[alias] = language;
        }
    }
} 