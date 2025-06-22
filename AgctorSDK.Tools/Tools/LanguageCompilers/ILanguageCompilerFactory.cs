namespace AgctorSDK.Core.Tools.LanguageCompilers
{
    /// <summary>
    /// Factory interface for retrieving or registering compilers for different languages.
    /// </summary>
    public interface ILanguageCompilerFactory
    {
        /// <summary>
        /// Gets a compiler capable of building the specified language, or <c>null</c> when unsupported.
        /// </summary>
        /// <param name="language">Language identifier (case-insensitive).</param>
        /// <returns>The compiler instance or <c>null</c>.</returns>
        ILanguageCompiler? GetCompiler(string language);

        /// <summary>
        /// Registers (or replaces) a compiler instance.
        /// </summary>
        /// <param name="compiler">Compiler to register.</param>
        void RegisterCompiler(ILanguageCompiler compiler);

        /// <summary>
        /// Registers an alias that maps to an existing language key (e.g., "c#" → "csharp").
        /// </summary>
        /// <param name="alias">Alias name.</param>
        /// <param name="language">Actual language identifier.</param>
        void RegisterLanguageAlias(string alias, string language);
    }
} 