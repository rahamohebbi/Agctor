using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using AgctorSDK.Host.Models;
using Xunit;
using FluentAssertions;
using System.Collections.Generic;

namespace AgctorSDK.Host.IntegrationTests
{
    /// <summary>
    /// End-to-end integration test that exercises the CodeGraph demo scenario via the HTTP API.
    /// </summary>
    public class CodeGraphScenarioIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public CodeGraphScenarioIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task SetupScenario_And_InvokeIndexerAgent_ShouldSucceed()
        {
            // 1. Setup the scenario via TestController.
            var setupRequest = new ScenarioSetupRequest("code-graph-demo", new Dictionary<string, object>());

            var setupResponse = await _client.PostAsJsonAsync("/api/test/setup-scenario", setupRequest);
            setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var setupPayload = await setupResponse.Content.ReadFromJsonAsync<ScenarioSetupResponse>();
            setupPayload!.Success.Should().BeTrue();
            setupPayload.CreatedAgentIds.Should().Contain("indexer-agent");

            // 2. Send a prompt to the IndexerAgent to trigger indexing.
            var messageRequest = new MessageRequest
            {
                Payload = "index", // any string will trigger IndexerAgent
                SenderId = "integration-test"
            };

            var msgResp = await _client.PostAsJsonAsync("/api/agents/indexer-agent/message", messageRequest);
            msgResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted);
        }
    }
} 