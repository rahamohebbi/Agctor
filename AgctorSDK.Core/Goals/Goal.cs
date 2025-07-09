using System;

namespace AgctorSDK.Core.Goals
{
    /// <summary>
    /// Domain entity representing a high-level goal submitted by an administrator. Persisted via <see cref="IGoalStore"/>.
    /// </summary>
    public class Goal
    {
        /// <summary>
        /// Unique identifier for the goal. Generated if not supplied.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Short human-readable title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Rich description explaining desired outcome.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Current lifecycle status.
        /// </summary>
        public GoalStatus Status { get; set; } = GoalStatus.Pending;
    }
} 