using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.LanguageExecutors
{
    /// <summary>
    /// Interface for language-specific code execution
    /// </summary>
    public interface ILanguageExecutor
    {
        /// <summary>
        /// Gets the language identifier supported by this executor
        /// </summary>
        string Language { get; }

        /// <summary>
        /// Executes code in the specific language
        /// </summary>
        /// <param name="code">The code to execute</param>
        /// <returns>A tuple containing success status, output, and error message if any</returns>
        Task<(bool Success, string Output, string Error)> ExecuteCodeAsync(string code);
    }
} 