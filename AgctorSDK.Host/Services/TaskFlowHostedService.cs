using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services;

public sealed class TaskFlowHostedService : IHostedService, IDisposable
{
    private readonly TaskFlowEngine _engine;
    private readonly ILogger<TaskFlowHostedService> _logger;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public TaskFlowHostedService(ITaskStore store, ILogger<TaskFlowHostedService> logger, IOptions<TaskFlowOptions>? opts = null)
    {
        _engine = new TaskFlowEngine(store, new SimpleTaskExecutor());
        _logger = logger;
        _interval = opts?.Value?.Interval ?? TimeSpan.FromSeconds(10);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TaskFlowHostedService starting, interval {Interval}s", _interval.TotalSeconds);
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await _engine.RunOnceAsync(token); }
            catch (Exception ex) { _logger.LogError(ex, "TaskFlowEngine error"); }
            try { await Task.Delay(_interval, token); } catch (TaskCanceledException) { }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        if (_loop != null) await Task.WhenAny(_loop, Task.Delay(Timeout.Infinite, cancellationToken));
    }

    public void Dispose() => _cts.Cancel();

    public class TaskFlowOptions
    {
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);
    }
} 