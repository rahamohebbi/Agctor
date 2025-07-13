using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Git;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tasks;
using Microsoft.Extensions.Logging;
using CoreTaskStatus = AgctorSDK.Core.Tasks.TaskStatus;
using AgctorSDK.Agents.Agents;

namespace AgctorSDK.Agents.Tasks;

/// <summary>
/// Executes tasks that instruct the system to create a pull-request.
/// Recognises tasks whose <see cref="ProjectTask.Title"/> starts with
/// "PR" / "Pull Request" *or* whose description contains the pipe-separated
/// prompt expected by <see cref="Agents.PullRequestAgent"/>.
/// For all other tasks this executor delegates to the configured
/// <c>fallback</c> executor (typically <see cref="CodeGraphTaskExecutor"/>),
/// so a single instance can handle heterogeneous task sets.
/// </summary>
public sealed class PullRequestTaskExecutor : ITaskExecutor
{
    // The well-known ID of the agent that performs the git workflow.
    private const string PullRequestAgentId = "pull-request-agent";

    private readonly IAgentRegistry _registry;
    private readonly IAgentFactory _factory;
    private readonly IGitService _git;
    private readonly ILogger<PullRequestTaskExecutor> _logger;
    private readonly ILogger<PullRequestAgent> _agentLogger;
    private readonly ITaskExecutor _fallback;

    public PullRequestTaskExecutor(
        IAgentRegistry registry,
        IAgentFactory factory,
        IGitService gitService,
        ILogger<PullRequestTaskExecutor> logger,
        ILogger<PullRequestAgent> agentLogger,
        ITaskExecutor fallback)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _git = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentLogger = agentLogger ?? throw new ArgumentNullException(nameof(agentLogger));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public async Task ExecuteAsync(ProjectTask task, CancellationToken cancellationToken = default)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));
        if (task.Status is CoreTaskStatus.Completed or CoreTaskStatus.Running) return;

        if (!IsPullRequestTask(task))
        {
            await _fallback.ExecuteAsync(task, cancellationToken);
            return;
        }

        task.Status = CoreTaskStatus.Running;

        try
        {
            var prompt = string.IsNullOrWhiteSpace(task.Description)
                ? task.Title
                : task.Description;

            // Ensure we have an agent instance. Spawn on-demand if missing.
            var agent = await _registry.GetAgentByIdAsync(PullRequestAgentId);
            if (agent == null)
            {
                _logger.LogInformation("Spawning {AgentId} for pull-request automation", PullRequestAgentId);
                await _factory.RuntimeAdapter.SpawnActorAsync<PullRequestAgent>(
                    PullRequestAgentId,
                    id => new PullRequestAgent(id, _git, _agentLogger));
            }

            var headers = new Dictionary<string, string>
            {
                ["SenderId"] = "task-flow-engine",
                ["ReceiverId"] = PullRequestAgentId,
                ["MessageType"] = "Prompt"
            };

            // Wait up to 10 minutes – opening a PR can be slow if network/CI delays.
            var prInfo = await _factory.RuntimeAdapter.SendMessageAsync<PullRequestInfo>(
                PullRequestAgentId,
                prompt,
                timeout: TimeSpan.FromMinutes(10),
                senderId: "task-flow-engine",
                headers: headers,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Pull-request created: {Url} (branch {Branch})", prInfo.Url, prInfo.BranchName);
            task.Status = CoreTaskStatus.Completed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PullRequestTaskExecutor failed for task {TaskId}", task.Id);
            task.Status = CoreTaskStatus.Failed;
        }
    }

    private static bool IsPullRequestTask(ProjectTask task)
    {
        if (task == null) return false;

        if (task.Title.StartsWith("PR", StringComparison.OrdinalIgnoreCase) ||
            task.Title.StartsWith("Pull Request", StringComparison.OrdinalIgnoreCase))
            return true;

        // Heuristic: description contains the pipe-separated format "branch|commit|…".
        return task.Description?.Contains('|') == true;
    }
} 