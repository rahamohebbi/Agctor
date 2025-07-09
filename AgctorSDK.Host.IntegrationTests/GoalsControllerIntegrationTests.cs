using System.Net;
using System.Net.Http.Json;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Goals;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Integration tests for the Goals REST API.
/// </summary>
public class GoalsControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static int _portCounter = 9000; // Dedicated range for goal tests

    public GoalsControllerIntegrationTests(WebApplicationFactory<Program> baseFactory)
    {
        _factory = baseFactory;
    }

    [Fact]
    public async Task GoalCrud_Flow_ShouldWorkAndPersistAcrossRestarts()
    {
        // Use a unique persistent JSON file so data can survive host restarts during the test
        var tempPath = Path.Combine(Path.GetTempPath(), $"goals-api-{Guid.NewGuid()}.json");

        // Factory #1 – start host, create a goal
        var factory1 = CreateFactory(tempPath);
        var client1 = factory1.CreateClient();

        var createRequest = new Goal
        {
            Title = "Integration Goal",
            Description = "Verify CRUD via API"
        };

        var createResponse = await client1.PostAsJsonAsync("/api/goals", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdGoal = await createResponse.Content.ReadFromJsonAsync<Goal>();
        createdGoal.Should().NotBeNull();
        var id = createdGoal!.Id;
        createdGoal.Title.Should().Be("Integration Goal");

        // Factory #2 – simulate restart and verify persistence
        await factory1.DisposeAsync();
        var factory2 = CreateFactory(tempPath);
        var client2 = factory2.CreateClient();

        var listResponse = await client2.GetAsync("/api/goals");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var goals = await listResponse.Content.ReadFromJsonAsync<IEnumerable<Goal>>();
        goals.Should().ContainSingle(g => g.Id == id);

        // Delete the goal
        var deleteResponse = await client2.DeleteAsync($"/api/goals/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm deletion
        var listAfterDelete = await client2.GetAsync("/api/goals");
        var goalsAfterDelete = await listAfterDelete.Content.ReadFromJsonAsync<IEnumerable<Goal>>();
        goalsAfterDelete.Should().NotContain(g => g.Id == id);
    }

    private WebApplicationFactory<Program> CreateFactory(string path)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                var uniquePort = Interlocked.Increment(ref _portCounter);
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString())
                });
            });

            builder.ConfigureServices(services =>
            {
                // Replace the default goal store with temp-path-backed instance
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IGoalStore));
                if (descriptor != null) services.Remove(descriptor);
                services.AddInMemoryGoalStore(path);
            });
        });
    }
} 