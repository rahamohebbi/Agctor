using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Tasks
{
    /// <summary>
    /// Represents a concrete unit of work derived from an admin-defined goal.
    /// </summary>
    public class ProjectTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GoalId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<Guid> Dependencies { get; set; } = new();
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
    }
} 