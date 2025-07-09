using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tasks
{
    public sealed class InMemoryTaskStore : ITaskStore
    {
        private readonly ConcurrentDictionary<Guid, ProjectTask> _tasks = new();
        private readonly string _filePath;
        private readonly JsonSerializerOptions _options = new(JsonSerializerOptions.Default) { WriteIndented = true };

        public InMemoryTaskStore(string? filePath = null)
        {
            _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "tasks.json");
            Load();
        }

        public Task<ProjectTask> CreateAsync(ProjectTask task)
        {
            if (task.Id == Guid.Empty) task.Id = Guid.NewGuid();
            if (!_tasks.TryAdd(task.Id, task))
                throw new InvalidOperationException($"Task with id {task.Id} already exists.");
            Persist();
            return Task.FromResult(task);
        }

        public Task<IEnumerable<ProjectTask>> GetAllAsync() => Task.FromResult(_tasks.Values.AsEnumerable());

        public Task<IEnumerable<ProjectTask>> GetByGoalAsync(Guid goalId)
        {
            var result = _tasks.Values.Where(t => t.GoalId == goalId);
            return Task.FromResult(result);
        }

        public Task<ProjectTask?> GetAsync(Guid id)
        {
            _tasks.TryGetValue(id, out var task);
            return Task.FromResult(task);
        }

        public Task UpdateAsync(ProjectTask task)
        {
            if (!_tasks.ContainsKey(task.Id))
                throw new KeyNotFoundException();
            _tasks[task.Id] = task;
            Persist();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id)
        {
            _tasks.TryRemove(id, out _);
            Persist();
            return Task.CompletedTask;
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<ProjectTask>>(json, _options) ?? new();
                foreach (var t in list) _tasks[t.Id] = t;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Could not load tasks from {_filePath}: {ex.Message}");
            }
        }

        private void Persist()
        {
            try
            {
                var tmp = _filePath + ".tmp";
                var json = JsonSerializer.Serialize(_tasks.Values.ToList(), _options);
                File.WriteAllText(tmp, json);
                File.Move(tmp, _filePath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Could not persist tasks to {_filePath}: {ex.Message}");
            }
        }
    }
} 