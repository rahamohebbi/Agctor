using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.LanguageCompilers
{
    /// <summary>
    /// Interface for language-specific compilation capability.
    /// </summary>
    public interface ILanguageCompiler
    {
        /// <summary>
        /// Gets the language identifier supported by this compiler (e.g., "csharp", "python").
        /// </summary>
        string Language { get; }

        /// <summary>
        /// Compiles the supplied source code and returns build diagnostics.
        /// </summary>
        /// <param name="code">The complete source code unit to compile.</param>
        /// <returns>
        /// Tuple <c>(Success, Output, Error)</c> where:
        ///   • <c>Success</c> – <see langword="true"/> if compilation succeeded.
        ///   • <c>Output</c> – Compiler stdout / diagnostic information.
        ///   • <c>Error</c>  – Error text when <c>Success</c> is <see langword="false"/>.
        /// </returns>
        Task<(bool Success, string Output, string Error)> CompileCodeAsync(string code);
    }
} 