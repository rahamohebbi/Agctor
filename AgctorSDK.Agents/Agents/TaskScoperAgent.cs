using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Goals;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Tasks;

namespace AgctorSDK.Agents.Agents
{
    /// <summary>
    /// Generates a directed acyclic task graph from new goals and stores the resulting tasks.
    /// </summary>
    public class TaskScoperAgent : Agent
    {
        private readonly IGoalStore _goalStore;
        private readonly ITaskStore _taskStore;

        public TaskScoperAgent(string id, IGoalStore goalStore, ITaskStore taskStore) : base(id)
        {
            _goalStore = goalStore;
            _taskStore = taskStore;
        }

        // The runtime can periodically send a tick message or invoke ProcessGoalsAsync to generate tasks.

        public async Task ProcessGoalsAsync()
        {
            var goals = await _goalStore.GetAllAsync();
            foreach (var goal in goals.Where(g => g.Status == GoalStatus.Pending))
            {
                var tasks = GenerateTasks(goal);
                foreach (var t in tasks)
                    await _taskStore.CreateAsync(t);

                goal.Status = GoalStatus.InProgress;
                await _goalStore.UpdateAsync(goal);
            }
        }

        /// <summary>
        /// Very simple heuristic: each non-empty line in the goal description represents a task. Dependencies are declared with ':'
        /// Example: "Task2:Task1,Task0" means Task2 depends on Task1 and Task0.
        /// </summary>
        public List<ProjectTask> GenerateTasks(Goal goal)
        {
            var lines = goal.Description.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var temp = new List<(string title, List<string> deps)>();
            foreach (var raw in lines)
            {
                var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
                var title = parts[0];
                var deps = parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList() : new();
                temp.Add((title, deps));
            }

            // First create tasks without dependencies to know their IDs
            var tasks = temp.Select(t => new ProjectTask
            {
                GoalId = goal.Id,
                Title = t.title,
                Description = t.title
            }).ToList();

            // Map dependencies by title → id
            foreach (var (tuple, idx) in temp.Select((t, i) => (t, i)))
            {
                var task = tasks[idx];
                task.Dependencies = tuple.deps
                    .Select(d => tasks.FirstOrDefault(x => string.Equals(x.Title, d, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .ToList();
            }
            return tasks;
        }
    }
} 