using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Goals
{
    /// <summary>
    /// Contract for persisting and retrieving <see cref="Goal"/> entities. Implementations SHOULD be thread-safe because ASP.NET controllers
    /// could serve concurrent requests.
    /// </summary>
    public interface IGoalStore
    {
        /// <summary>
        /// Persists a new goal.
        /// </summary>
        /// <param name="goal">Goal object. <see cref="Goal.Id"/> will be overwritten if empty.</param>
        Task<Goal> CreateAsync(Goal goal);

        /// <summary>
        /// Returns all stored goals.
        /// </summary>
        Task<IEnumerable<Goal>> GetAllAsync();

        /// <summary>
        /// Returns a single goal or <c>null</c> when not found.
        /// </summary>
        Task<Goal?> GetAsync(Guid id);

        /// <summary>
        /// Updates an existing goal. Throws <see cref="KeyNotFoundException"/> if the goal is missing.
        /// </summary>
        Task UpdateAsync(Goal goal);

        /// <summary>
        /// Deletes a goal. No-op if not found.
        /// </summary>
        Task DeleteAsync(Guid id);
    }
} 