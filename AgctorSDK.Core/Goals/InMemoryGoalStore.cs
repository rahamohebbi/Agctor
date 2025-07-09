using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Goals
{
    /// <summary>
    /// Simple in-memory <see cref="IGoalStore"/> that also persists goals to a JSON file on disk so data survives restarts.
    /// Serialized file is written atomically to avoid corruption.
    /// </summary>
    public sealed class InMemoryGoalStore : IGoalStore
    {
        private readonly ConcurrentDictionary<Guid, Goal> _goals = new();
        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerOptions.Default)
        {
            WriteIndented = true
        };

        public InMemoryGoalStore(string? filePath = null)
        {
            _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "goals.json");
            LoadFromDisk();
        }

        public Task<Goal> CreateAsync(Goal goal)
        {
            if (goal.Id == Guid.Empty)
                goal.Id = Guid.NewGuid();

            if (!_goals.TryAdd(goal.Id, goal))
                throw new InvalidOperationException($"Goal with id {goal.Id} already exists.");

            PersistAsync().ConfigureAwait(false);
            return Task.FromResult(goal);
        }

        public Task<IEnumerable<Goal>> GetAllAsync() => Task.FromResult(_goals.Values.AsEnumerable());

        public Task<Goal?> GetAsync(Guid id)
        {
            _goals.TryGetValue(id, out var goal);
            return Task.FromResult(goal);
        }

        public Task UpdateAsync(Goal goal)
        {
            if (!_goals.ContainsKey(goal.Id))
                throw new KeyNotFoundException($"Goal with id {goal.Id} does not exist.");

            _goals[goal.Id] = goal;
            PersistAsync().ConfigureAwait(false);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id)
        {
            _goals.TryRemove(id, out _);
            PersistAsync().ConfigureAwait(false);
            return Task.CompletedTask;
        }

        #region Persistence helpers

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return;

                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<Goal>>(json, _jsonOptions) ?? new List<Goal>();
                foreach (var g in items)
                {
                    _goals.TryAdd(g.Id, g);
                }
            }
            catch (Exception ex)
            {
                // Log warning but continue (this is core library, so using Console). In production, ILogger would be used.
                Console.WriteLine($"⚠️ Failed to load goals from {_filePath}: {ex.Message}");
            }
        }

        private async Task PersistAsync()
        {
            try
            {
                var tmpPath = _filePath + ".tmp";
                var json = JsonSerializer.Serialize(_goals.Values.ToList(), _jsonOptions);
                await File.WriteAllTextAsync(tmpPath, json);
                File.Move(tmpPath, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to persist goals to {_filePath}: {ex.Message}");
            }
        }

        #endregion
    }
} 