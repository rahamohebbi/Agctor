using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.Abstractions
{
    /// <summary>
    /// Defines a language-specific code formatter.
    /// </summary>
    public interface ICodeFormatter
    {
        /// <summary>
        /// Programming language identifier (e.g. "csharp", "python"). Lower-case.
        /// </summary>
        string Language { get; }

        /// <summary>
        /// Formats the supplied code returning the prettified version or an error.
        /// </summary>
        /// <param name="code">Raw source code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Tuple (isSuccess, formattedCode, error)</returns>
        Task<(bool IsSuccess, string? FormattedCode, string? Error)> FormatAsync(string code, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns true if the underlying formatting engine is available on the host.
        /// </summary>
        bool IsAvailable { get; }
    }
} 