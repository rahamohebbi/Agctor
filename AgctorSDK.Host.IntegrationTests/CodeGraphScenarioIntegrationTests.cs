using System.Net;
using System.Net.Http.Json;
using System.Threading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using AgctorSDK.Host.Models;
using Xunit;
using FluentAssertions;
using System.Collections.Generic;

namespace AgctorSDK.Host.IntegrationTests
{
    /// <summary>
    /// End-to-end integration test that exercises the CodeGraph demo scenario via the HTTP API.
    /// </summary>
    public class CodeGraphScenarioIntegrationTests : IClassFixture<AgctorWebApplicationFactory>
    {
        private readonly HttpClient _client;
        private static int _portCounter = 14280;

        public CodeGraphScenarioIntegrationTests(AgctorWebApplicationFactory factory)
        {
            var configured = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var uniquePort = Interlocked.Increment(ref _portCounter);
                    config.AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string?>("Mcp:Port", uniquePort.ToString())
                    });
                });
            });
            _client = configured.CreateClient();
        }

        [Fact]
        public async Task SetupScenario_And_InvokeIndexerAgent_ShouldSucceed()
        {
            // 1. Setup the scenario via TestController.
            var setupRequest = new ScenarioSetupRequest("code-graph-demo", new Dictionary<string, object>
            {
                ["useStubEmbeddings"] = true
            });

            var setupResponse = await _client.PostAsJsonAsync("/api/test/setup-scenario", setupRequest);
            setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var setupPayload = await setupResponse.Content.ReadFromJsonAsync<ScenarioSetupResponse>();
            setupPayload!.Success.Should().BeTrue();
            setupPayload.CreatedAgentIds.Should().Contain("indexer-agent");
            setupPayload.CreatedAgentIds.Should().Contain("embedding-coordinator-agent");
            setupPayload.CreatedAgentIds.Should().Contain("intent-agent");

            // 2. Send a prompt to the IndexerAgent to trigger indexing.
            var messageRequest = new MessageRequest
            {
                Payload = "index", // any string will trigger IndexerAgent
                SenderId = "integration-test"
            };

            var msgResp = await _client.PostAsJsonAsync("/api/agents/indexer-agent/message", messageRequest);
            msgResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted);
        }

        [Fact]
        public async Task SetupScenario_And_SearchAgent_ShouldAutoIndexAndExposeReadyStatus()
        {
            var setupRequest = new ScenarioSetupRequest("code-graph-demo", new Dictionary<string, object>
            {
                ["useStubEmbeddings"] = true
            });

            var setupResponse = await _client.PostAsJsonAsync("/api/test/setup-scenario", setupRequest);
            setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var before = await _client.GetFromJsonAsync<CodeGraphContextDto>("/api/CodeGraph/current");
            before.Should().NotBeNull();
            before!.EmbeddingStoreSummary.Should().NotBeNull();
            before.EmbeddingStoreSummary!.State.Should().Be("NotReady");

            var messageRequest = new MessageRequest
            {
                Payload = "Login",
                SenderId = "integration-test"
            };

            var searchResponse = await _client.PostAsJsonAsync("/api/agents/search-agent/message", messageRequest);
            searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var searchBody = await searchResponse.Content.ReadAsStringAsync();
            searchBody.Should().Contain("responseData");
            searchBody.Should().NotContain("No code found");

            var after = await _client.GetFromJsonAsync<CodeGraphContextDto>("/api/CodeGraph/current");
            after.Should().NotBeNull();
            after!.EmbeddingStoreSummary.Should().NotBeNull();
            after.EmbeddingStoreSummary!.State.Should().Be("Ready");
            after.EmbeddingStoreSummary.IsReady.Should().BeTrue();
            after.EmbeddingStoreSummary.VectorCount.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task SetupScenario_CanBeReapplied_AndStillIncludesEmbeddingCoordinatorAgent()
        {
            var setupRequest = new ScenarioSetupRequest("code-graph-demo", new Dictionary<string, object>
            {
                ["useStubEmbeddings"] = true
            });

            var first = await _client.PostAsJsonAsync("/api/test/setup-scenario", setupRequest);
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            var firstPayload = await first.Content.ReadFromJsonAsync<ScenarioSetupResponse>();
            firstPayload!.Success.Should().BeTrue();
            firstPayload.CreatedAgentIds.Should().Contain("embedding-coordinator-agent");
            firstPayload.CreatedAgentIds.Should().Contain("intent-agent");

            var second = await _client.PostAsJsonAsync("/api/test/setup-scenario", setupRequest);
            second.StatusCode.Should().Be(HttpStatusCode.OK);
            var secondPayload = await second.Content.ReadFromJsonAsync<ScenarioSetupResponse>();
            secondPayload!.Success.Should().BeTrue();
            secondPayload.CreatedAgentIds.Should().Contain("embedding-coordinator-agent");
            secondPayload.CreatedAgentIds.Should().Contain("intent-agent");
            secondPayload.CreatedAgentIds.Should().Contain("session-coordinator-agent");
            secondPayload.CreatedAgentIds.Count.Should().Be(9);

            var agents = await _client.GetFromJsonAsync<List<AgentInfo>>("/api/agents");
            agents.Should().NotBeNull();
            agents!.Select(a => a.Id).Should().Contain("embedding-coordinator-agent");
            agents.Select(a => a.Id).Should().Contain("intent-agent");
            agents.Select(a => a.Id).Should().Contain("query-agent");
            agents.Select(a => a.Id).Should().Contain("session-coordinator-agent");
        }
    }
} 