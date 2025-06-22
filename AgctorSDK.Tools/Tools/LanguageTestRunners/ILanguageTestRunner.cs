using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.LanguageTestRunners
{
    /// <summary>
    /// Provides the capability to execute a test suite for a given programming language.
    /// </summary>
    public interface ILanguageTestRunner
    {
        /// <summary>
        /// Language identifier this runner supports (e.g., "csharp", "python").
        /// </summary>
        string Language { get; }

        /// <summary>
        /// Executes the test suite found at the given path.
        /// For C# this could be a *.csproj directory; for Python, a folder with tests.
        /// </summary>
        /// <param name="path">Path pointing to the project or directory containing tests.</param>
        /// <returns>Tuple (Success, Output, Error).</returns>
        Task<(bool Success, string Output, string Error)> RunTestsAsync(string path);
    }
} 