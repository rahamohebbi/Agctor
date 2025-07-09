using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tasks
{
    /// <summary>
    /// A no-op executor that marks tasks as completed after a tiny delay. Useful for initial testing.
    /// </summary>
    public sealed class SimpleTaskExecutor : ITaskExecutor
    {
        public async Task ExecuteAsync(ProjectTask task, CancellationToken cancellationToken = default)
        {
            // Simulate work
            await Task.Delay(50, cancellationToken);
            task.Status = TaskStatus.Completed;
        }
    }
} 