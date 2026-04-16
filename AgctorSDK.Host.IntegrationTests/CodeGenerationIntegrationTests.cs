using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AgctorSDK.Core.Coding;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Goals;
using AgctorSDK.Core.Tasks;
using AgctorSDK.Agents.Agents;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using CoreTaskStatus = AgctorSDK.Core.Tasks.TaskStatus;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Ensures the CoderTaskExecutor + SimpleCodeGenerator produce an output file end-to-end.
/// </summary>
public class CodeGenerationIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
{
    private readonly AgctorWebApplicationFactory _baseFactory;
    private static int _port = 9200;

    public CodeGenerationIntegrationTests(AgctorWebApplicationFactory factory) => _baseFactory = factory;

    [Fact(Timeout = 20000)]
    public async Task GoalFlow_ShouldGenerateFile()
    {
        var goalPath = Path.Combine(Path.GetTempPath(), $"cg-goals-{Guid.NewGuid()}.json");
        var taskPath = Path.Combine(Path.GetTempPath(), $"cg-tasks-{Guid.NewGuid()}.json");
        var outputDir = Path.Combine(Path.GetTempPath(), $"cg-output-{Guid.NewGuid()}");

        using var factory = CreateFactory(goalPath, taskPath, outputDir);
        var client = factory.CreateClient();

        // One simple task ("Gen")
        var goalReq = new Goal { Title = "CG", Description = "Gen" };
        var resp = await client.PostAsJsonAsync("/api/goals", goalReq);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<Goal>();
        created.Should().NotBeNull();
        var goalId = created!.Id;

        // Drive scoping + execution manually for determinism
        using var scope = factory.Services.CreateScope();
        var goalStore = scope.ServiceProvider.GetRequiredService<IGoalStore>();
        var taskStore = scope.ServiceProvider.GetRequiredService<ITaskStore>();

        var scoper = new TaskScoperAgent("scoper-int", goalStore, taskStore);
        await scoper.ProcessGoalsAsync();

        var engine = new TaskFlowEngine(taskStore, new CoderTaskExecutor(new SimpleCodeGenerator(outputDir)));
        await engine.RunOnceAsync();

        // Assert task completed and file exists
        var tasks = (await taskStore.GetByGoalAsync(goalId)).ToList();
        tasks.Should().ContainSingle().Which.Status.Should().Be(CoreTaskStatus.Completed);
        Directory.Exists(outputDir).Should().BeTrue();
        Directory.GetFiles(outputDir).Should().ContainSingle();
    }

    private WebApplicationFactory<Program> CreateFactory(string goalJson, string taskJson, string outputDir)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string,string?>("Mcp:Port", (_port++).ToString())
                });
            });

            builder.ConfigureServices(services =>
            {
                // replace stores
                services.RemoveAll(typeof(IGoalStore));
                services.AddInMemoryGoalStore(goalJson);
                services.RemoveAll(typeof(ITaskStore));
                services.AddInMemoryTaskStore(taskJson);

                // replace code generation with fixed output dir
                services.RemoveAll(typeof(ICodeGenerator));
                services.RemoveAll(typeof(ITaskExecutor));
                services.AddSingleton<ICodeGenerator>(_ => new SimpleCodeGenerator(outputDir));
                services.AddSingleton<ITaskExecutor, CoderTaskExecutor>();
            });
        });
    }
} 