using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tasks
{
    public interface ITaskStore
    {
        Task<ProjectTask> CreateAsync(ProjectTask task);
        Task<IEnumerable<ProjectTask>> GetAllAsync();
        Task<IEnumerable<ProjectTask>> GetByGoalAsync(Guid goalId);
        Task<ProjectTask?> GetAsync(Guid id);
        Task UpdateAsync(ProjectTask task);
        Task DeleteAsync(Guid id);
    }
} 