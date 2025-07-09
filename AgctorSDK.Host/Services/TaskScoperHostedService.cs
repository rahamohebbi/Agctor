using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Agents.Agents;
using AgctorSDK.Core.Goals;
using AgctorSDK.Core.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Periodically invokes <see cref="TaskScoperAgent"/> to convert new goals into task DAGs (Directed Acyclic Graphs).
/// </summary>
public sealed class TaskScoperHostedService : IHostedService, IDisposable
{
    private readonly IGoalStore _goalStore;
    private readonly ITaskStore _taskStore;
    private readonly ILogger<TaskScoperHostedService> _logger;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private readonly TaskScoperAgent _agent;

    public TaskScoperHostedService(IGoalStore goalStore, ITaskStore taskStore, ILogger<TaskScoperHostedService> logger, IOptions<TaskScoperOptions>? options = null)
    {
        _goalStore = goalStore;
        _taskStore = taskStore;
        _logger = logger;
        _interval = options?.Value?.ScanInterval ?? TimeSpan.FromSeconds(30);
        _agent = new TaskScoperAgent("task-scoper", _goalStore, _taskStore);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TaskScoperHostedService starting with interval {Interval}s", _interval.TotalSeconds);
        _loopTask = Task.Run(() => RunAsync(_cts.Token));
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _agent.ProcessGoalsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TaskScoperAgent processing failed");
            }

            try
            {
                await Task.Delay(_interval, token);
            }
            catch (TaskCanceledException) { }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        if (_loopTask != null)
        {
            await Task.WhenAny(_loopTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    public class TaskScoperOptions
    {
        public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(30);
    }
} 