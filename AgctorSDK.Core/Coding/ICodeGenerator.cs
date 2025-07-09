using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Tasks;

namespace AgctorSDK.Core.Coding
{
    /// <summary>
    /// Generates or modifies source code to satisfy a <see cref="ProjectTask"/>.
    /// Implementations may leverage templates, heuristics, or LLMs.
    /// </summary>
    public interface ICodeGenerator
    {
        /// <summary>
        /// Generates code for the given task.
        /// </summary>
        /// <param name="task">The task describing required work.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="CodeGenerationResult"/> detailing patches.</returns>
        Task<CodeGenerationResult> GenerateAsync(ProjectTask task, CancellationToken cancellationToken = default);
    }
} 