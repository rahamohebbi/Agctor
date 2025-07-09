using System.Net.Http.Json;
using AgctorSDK.Core.Goals;
using AgctorSDK.Core.Tasks;
using AgctorSDK.Core.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.IO;
using System.Linq;
using System.Threading;
using CoreTaskStatus = AgctorSDK.Core.Tasks.TaskStatus;
using AgctorSDK.Agents.Agents;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Verifies the full pipeline Goal -> Task DAG generation -> Task execution via TaskFlowEngine.
/// </summary>
public class TaskFlowEndToEndIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;
    private static int _portCounter = 9100;

    public TaskFlowEndToEndIntegrationTests(WebApplicationFactory<Program> baseFactory) => _baseFactory = baseFactory;

    [Fact(Timeout = 30000)]
    public async Task Goal_ShouldReachCompletedTasksState()
    {
        var goalPath = Path.Combine(Path.GetTempPath(), $"goals-e2e-{Guid.NewGuid()}.json");
        var taskPath = Path.Combine(Path.GetTempPath(), $"tasks-e2e-{Guid.NewGuid()}.json");

        using var factory = CreateFactory(goalPath, taskPath);
        var client = factory.CreateClient();

        // Create a goal with a small DAG: A -> B, C ; D depends on B and C
        var goalReq = new Goal
        {
            Title = "E2E Goal",
            Description = "A\nB:A\nC:A\nD:B,C"
        };

        var resp = await client.PostAsJsonAsync("/api/goals", goalReq);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<Goal>();
        created.Should().NotBeNull();
        var goalId = created!.Id;

        using var scope = factory.Services.CreateScope();
        var goalStore = scope.ServiceProvider.GetRequiredService<IGoalStore>();
        var taskStore = scope.ServiceProvider.GetRequiredService<ITaskStore>();

        // Manually invoke TaskScoperAgent and TaskFlowEngine to keep the test fast/deterministic
        var scoper = new TaskScoperAgent("scoper-test", goalStore, taskStore);
        await scoper.ProcessGoalsAsync();

        var engine = new TaskFlowEngine(taskStore, new SimpleTaskExecutor(), maxParallelism: 4);
        // Run the engine until no pending/running tasks remain or timeout reached
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await engine.RunOnceAsync();
            var done = (await taskStore.GetByGoalAsync(goalId)).ToList();
            if (done.Any() && done.All(t => t.Status == CoreTaskStatus.Completed))
            {
                return; // success
            }
            await Task.Delay(100);
        }

        var final = (await taskStore.GetByGoalAsync(goalId)).ToList();
        final.Should().NotBeEmpty();
        final.Should().AllSatisfy(t => t.Status.Should().Be(CoreTaskStatus.Completed));
    }

    private WebApplicationFactory<Program> CreateFactory(string goalPath, string taskPath)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var port = Interlocked.Increment(ref _portCounter);
                cfg.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string,string?>("Mcp:Port", port.ToString()),
                    new KeyValuePair<string,string?>("TaskScoper:ScanInterval", "1"),
                    new KeyValuePair<string,string?>("TaskFlow:Interval", "1")
                });
            });

            builder.ConfigureServices(services =>
            {
                // Replace goal store
                var gDesc = services.Single(d => d.ServiceType == typeof(IGoalStore));
                services.Remove(gDesc);
                services.AddInMemoryGoalStore(goalPath);

                // Replace task store
                var tDesc = services.SingleOrDefault(d => d.ServiceType == typeof(ITaskStore));
                if (tDesc != null) services.Remove(tDesc);
                services.AddInMemoryTaskStore(taskPath);
            });
        });
    }
} 