using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tasks
{
    /// <summary>
    /// Abstraction for executing a <see cref="ProjectTask"/>. Different executors can be plugged in (coder agent, external tool, etc.).
    /// </summary>
    public interface ITaskExecutor
    {
        /// <summary>
        /// Executes the provided task and returns when done.
        /// The implementation is responsible for updating task status in the store.
        /// </summary>
        Task ExecuteAsync(ProjectTask task, CancellationToken cancellationToken = default);
    }
} 