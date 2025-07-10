using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tasks;
using Microsoft.Extensions.Logging;
using CoreTaskStatus = AgctorSDK.Core.Tasks.TaskStatus;

namespace AgctorSDK.Agents.Tasks
{
    /// <summary>
    /// Task executor that sends the task description to the CodeGraph <c>RefactorAgent</c>.
    /// If that agent is not available, the executor falls back to <see cref="SimpleTaskExecutor"/> so
    /// development environments without the CodeGraph scenario still function.
    /// </summary>
    public sealed class CodeGraphTaskExecutor : ITaskExecutor
    {
        private readonly IAgentRegistry _registry;
        private readonly IAgentFactory _factory;
        private readonly ILogger<CodeGraphTaskExecutor> _logger;
        private readonly ITaskExecutor _fallback;

        private const string RefactorAgentId = "refactor-agent";

        public CodeGraphTaskExecutor(
            IAgentRegistry registry,
            IAgentFactory factory,
            ILogger<CodeGraphTaskExecutor> logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _fallback = new SimpleTaskExecutor();
        }

        public async Task ExecuteAsync(ProjectTask task, CancellationToken cancellationToken = default)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (task.Status is CoreTaskStatus.Completed or CoreTaskStatus.Running) return;

            task.Status = CoreTaskStatus.Running;

            try
            {
                var agent = await _registry.GetAgentByIdAsync(RefactorAgentId);
                if (agent == null)
                {
                    _logger.LogWarning("RefactorAgent not found – using SimpleTaskExecutor for task {TaskId}", task.Id);
                    await _fallback.ExecuteAsync(task, cancellationToken);
                    return;
                }

                var prompt = string.IsNullOrWhiteSpace(task.Description)
                    ? task.Title
                    : $"{task.Title}\n{task.Description}";

                var headers = new Dictionary<string, string>
                {
                    ["SenderId"] = "task-flow-engine",
                    ["ReceiverId"] = RefactorAgentId,
                    ["MessageType"] = "Prompt"
                };

                var result = await _factory.RuntimeAdapter.SendMessageAsync<string>(
                    RefactorAgentId,
                    prompt,
                    timeout: TimeSpan.FromMinutes(15),
                    senderId: "task-flow-engine",
                    headers: headers,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "RefactorAgent completed task {TaskId}. Result preview: {Preview}",
                    task.Id,
                    result?.Substring(0, Math.Min(120, result.Length)));

                task.Status = CoreTaskStatus.Completed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CodeGraphTaskExecutor failed for task {TaskId}", task.Id);
                task.Status = CoreTaskStatus.Failed;
            }
        }
    }
} 