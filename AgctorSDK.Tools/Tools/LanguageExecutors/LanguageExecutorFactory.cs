using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.Tools.LanguageExecutors
{
    /// <summary>
    /// Factory for creating language-specific code executors
    /// </summary>
    public class LanguageExecutorFactory : ILanguageExecutorFactory
    {
        private readonly Dictionary<string, ILanguageExecutor> _executors;
        private readonly Dictionary<string, string> _languageAliases;

        /// <summary>
        /// Initializes a new instance of the LanguageExecutorFactory class with default executors
        /// </summary>
        public LanguageExecutorFactory()
        {
            _executors = new Dictionary<string, ILanguageExecutor>(StringComparer.OrdinalIgnoreCase);
            _languageAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            // Register built-in executors
            RegisterExecutor(new CSharpExecutor());
            RegisterExecutor(new PythonExecutor());
            
            // Register common language aliases
            RegisterLanguageAlias("c#", "csharp");
            RegisterLanguageAlias("cs", "csharp");
            RegisterLanguageAlias("py", "python");
            RegisterLanguageAlias("python2", "python");
            RegisterLanguageAlias("python3", "python");
        }

        /// <summary>
        /// Gets a language executor for the specified language
        /// </summary>
        /// <param name="language">The language identifier</param>
        /// <returns>An ILanguageExecutor instance or null if the language is not supported</returns>
        public ILanguageExecutor? GetExecutor(string language)
        {
            if (string.IsNullOrEmpty(language))
                return null;
                
            // Check if this is an alias
            if (_languageAliases.TryGetValue(language, out string? actualLanguage))
            {
                language = actualLanguage;
            }
            
            // Return the executor if found
            if (_executors.TryGetValue(language, out ILanguageExecutor? executor))
            {
                return executor;
            }
            
            return null;
        }
        
        /// <summary>
        /// Registers a new language executor
        /// </summary>
        /// <param name="executor">The executor to register</param>
        public void RegisterExecutor(ILanguageExecutor executor)
        {
            if (executor == null)
                throw new ArgumentNullException(nameof(executor));
                
            _executors[executor.Language] = executor;
        }
        
        /// <summary>
        /// Registers a language alias that maps to an actual language
        /// </summary>
        /// <param name="alias">The alias name (e.g., "c#")</param>
        /// <param name="language">The actual language name (e.g., "csharp")</param>
        public void RegisterLanguageAlias(string alias, string language)
        {
            if (string.IsNullOrEmpty(alias))
                throw new ArgumentNullException(nameof(alias));
                
            if (string.IsNullOrEmpty(language))
                throw new ArgumentNullException(nameof(language));
                
            _languageAliases[alias] = language;
        }
    }
} 