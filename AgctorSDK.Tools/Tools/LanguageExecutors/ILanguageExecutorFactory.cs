namespace AgctorSDK.Core.Tools.LanguageExecutors
{
    /// <summary>
    /// Factory interface for creating language-specific code executors
    /// </summary>
    public interface ILanguageExecutorFactory
    {
        /// <summary>
        /// Gets a language executor for the specified language
        /// </summary>
        /// <param name="language">The language identifier</param>
        /// <returns>An ILanguageExecutor instance or null if the language is not supported</returns>
        ILanguageExecutor? GetExecutor(string language);
        
        /// <summary>
        /// Registers a new language executor
        /// </summary>
        /// <param name="executor">The executor to register</param>
        void RegisterExecutor(ILanguageExecutor executor);
    }
} 