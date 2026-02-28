using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Goals;

namespace AgctorSDK.Core.Tasks
{
    /// <summary>
    /// Picks ready tasks (all dependencies completed) from the <see cref="ITaskStore"/>, executes them via <see cref="ITaskExecutor"/>, and updates status.
    /// When <see cref="IGoalStore"/> is provided, tasks whose goal is Paused or Cancelled are skipped each interval.
    /// </summary>
    public sealed class TaskFlowEngine
    {
        private readonly ITaskStore _taskStore;
        private readonly ITaskExecutor _executor;
        private readonly IGoalStore? _goalStore;
        private readonly int _maxParallelism;

        public TaskFlowEngine(ITaskStore taskStore, ITaskExecutor executor, int maxParallelism = 4)
            : this(taskStore, executor, goalStore: null, maxParallelism) { }

        public TaskFlowEngine(ITaskStore taskStore, ITaskExecutor executor, IGoalStore? goalStore, int maxParallelism = 4)
        {
            _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _goalStore = goalStore;
            _maxParallelism = Math.Max(1, maxParallelism);
        }

        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            var all = (await _taskStore.GetAllAsync()).ToList();
            var completedIds = new HashSet<Guid>(all.Where(t => t.Status == TaskStatus.Completed).Select(t => t.Id));

            var ready = all.Where(t => t.Status == TaskStatus.Pending && t.Dependencies.All(d => completedIds.Contains(d))).ToList();

            // Skip tasks whose goal is Paused or Cancelled
            if (_goalStore != null && ready.Count > 0)
            {
                var goals = await _goalStore.GetAllAsync();
                var skippedGoalIds = new HashSet<Guid>(goals
                    .Where(g => g.Status == GoalStatus.Paused || g.Status == GoalStatus.Cancelled)
                    .Select(g => g.Id));
                if (skippedGoalIds.Count > 0)
                    ready = ready.Where(t => !skippedGoalIds.Contains(t.GoalId)).ToList();
            }

            if (!ready.Any()) return;

            var throttler = new SemaphoreSlim(_maxParallelism);
            var tasks = ready.Select(async pt =>
            {
                await throttler.WaitAsync(ct);
                try
                {
                    pt.Status = TaskStatus.Running;
                    await _taskStore.UpdateAsync(pt);

                    await _executor.ExecuteAsync(pt, ct);
                    await _taskStore.UpdateAsync(pt);
                }
                catch (Exception)
                {
                    pt.Status = TaskStatus.Failed;
                    await _taskStore.UpdateAsync(pt);
                }
                finally
                {
                    throttler.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }
    }
} 