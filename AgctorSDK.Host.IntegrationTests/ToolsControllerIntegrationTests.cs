using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Controllers;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.IntegrationTests
{
    /// <summary>
    /// Integration tests for the ToolsController.
    /// Tests tool invocation, discovery, batch operations, and error handling.
    /// </summary>
    public class ToolsControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public ToolsControllerIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task InvokeTool_FileSystemTool_ReturnsSuccess()
        {
            // Arrange
            var toolId = "file-system";
            var request = new ToolInvocationRequest
            {
                Parameters = new Dictionary<string, object>
                {
                    ["operation"] = "list",
                    ["path"] = "/tmp"
                },
                Context = new Dictionary<string, object>
                {
                    ["user"] = "integration-test"
                },
                TimeoutSeconds = 10
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var toolResponse = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
            toolResponse.Should().NotBeNull();
            toolResponse!.InvocationId.Should().NotBeNullOrEmpty();
            toolResponse.Status.Should().Be(ToolExecutionStatus.Success);
            toolResponse.Result.Should().NotBeNull();
            toolResponse.ExecutionTimeMs.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task InvokeTool_CodeExecutorTool_ReturnsSuccess()
        {
            // Arrange
            var toolId = "code-executor";
            var request = new ToolInvocationRequest
            {
                Parameters = new Dictionary<string, object>
                {
                    ["language"] = "python",
                    ["code"] = "print('Hello from integration test!')",
                    ["timeout"] = 5
                },
                TimeoutSeconds = 15
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var toolResponse = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
            toolResponse.Should().NotBeNull();
            toolResponse!.Status.Should().Be(ToolExecutionStatus.Success);
            toolResponse.Result.Should().NotBeNull();
        }

        [Fact]
        public async Task InvokeTool_CodeEditorTool_ReturnsSuccess()
        {
            // Arrange
            var toolId = "code-editor";
            var request = new ToolInvocationRequest
            {
                Parameters = new Dictionary<string, object>
                {
                    ["operation"] = "format",
                    ["file"] = "test.cs",
                    ["changes"] = new { format = "standard" }
                }
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var toolResponse = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
            toolResponse.Should().NotBeNull();
            toolResponse!.Status.Should().Be(ToolExecutionStatus.Success);
        }

        [Fact]
        public async Task InvokeTool_NonExistentTool_ReturnsNotFound()
        {
            // Arrange
            var toolId = "non-existent-tool";
            var request = new ToolInvocationRequest
            {
                Parameters = new Dictionary<string, object>
                {
                    ["param1"] = "value1"
                }
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            
            var errorResponse = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            errorResponse.Should().NotBeNull();
            errorResponse!.Code.Should().Be("TOOL_NOT_FOUND");
        }

        [Fact]
        public async Task InvokeTool_EmptyParameters_ReturnsBadRequest()
        {
            // Arrange
            var toolId = "file-system";
            var request = new ToolInvocationRequest
            {
                Parameters = new Dictionary<string, object>() // Empty parameters
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            
            var errorResponse = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            errorResponse.Should().NotBeNull();
            errorResponse!.Code.Should().Be("INVALID_PARAMETERS");
        }

        [Fact]
        public async Task InvokeTool_NullParameters_ReturnsBadRequest()
        {
            // Arrange
            var toolId = "file-system";
            var request = new ToolInvocationRequest
            {
                Parameters = null!
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            
            var errorResponse = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            errorResponse.Should().NotBeNull();
            errorResponse!.Code.Should().Be("INVALID_PARAMETERS");
        }

        [Fact]
        public async Task GetTools_ReturnsAvailableTools()
        {
            // Act
            var response = await _client.GetAsync("/api/tools");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var tools = await response.Content.ReadFromJsonAsync<IEnumerable<string>>();
            tools.Should().NotBeNull();
            tools.Should().NotBeEmpty();
            tools.Should().Contain("file-system");
            tools.Should().Contain("code-executor");
            tools.Should().Contain("code-editor");
        }

        [Fact]
        public async Task GetToolInfo_FileSystemTool_ReturnsToolInfo()
        {
            // Arrange
            var toolId = "file-system";

            // Act
            var response = await _client.GetAsync($"/api/tools/{toolId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var toolInfo = await response.Content.ReadFromJsonAsync<ToolInfo>();
            toolInfo.Should().NotBeNull();
            toolInfo!.Id.Should().Be(toolId);
            toolInfo.Name.Should().NotBeNullOrEmpty();
            toolInfo.Description.Should().NotBeNullOrEmpty();
            toolInfo.Parameters.Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetToolInfo_NonExistentTool_ReturnsNotFound()
        {
            // Arrange
            var toolId = "non-existent-tool";

            // Act
            var response = await _client.GetAsync($"/api/tools/{toolId}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            
            var errorResponse = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            errorResponse.Should().NotBeNull();
            errorResponse!.Code.Should().Be("TOOL_NOT_FOUND");
        }

        [Fact]
        public async Task GetToolsHealth_ReturnsHealthStatus()
        {
            // Act
            var response = await _client.GetAsync("/api/tools/health");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var healthInfo = await response.Content.ReadFromJsonAsync<JsonElement>();
            healthInfo.GetProperty("status").GetString().Should().Be("healthy");
            healthInfo.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
            healthInfo.TryGetProperty("tools", out var toolsProperty).Should().BeTrue();
        }

        [Fact]
        public async Task BatchInvokeTools_ValidRequests_ReturnsSuccessfulResults()
        {
            // Arrange
            var batchRequest = new BatchToolInvocationRequest
            {
                Tools = new List<SingleToolInvocation>
                {
                    new()
                    {
                        ToolId = "file-system",
                        Request = new ToolInvocationRequest
                        {
                            Parameters = new Dictionary<string, object>
                            {
                                ["operation"] = "list",
                                ["path"] = "/home"
                            }
                        }
                    },
                    new()
                    {
                        ToolId = "code-executor",
                        Request = new ToolInvocationRequest
                        {
                            Parameters = new Dictionary<string, object>
                            {
                                ["language"] = "python",
                                ["code"] = "print('Batch execution test')"
                            }
                        }
                    }
                },
                StopOnError = false
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/tools/batch", batchRequest);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var batchResponse = await response.Content.ReadFromJsonAsync<IEnumerable<ToolInvocationResponse>>();
            batchResponse.Should().NotBeNull();
            batchResponse.Should().HaveCount(2);
            batchResponse.Should().OnlyContain(r => r.Status == ToolExecutionStatus.Success);
        }

        [Fact]
        public async Task BatchInvokeTools_EmptyBatch_ReturnsBadRequest()
        {
            // Arrange
            var batchRequest = new BatchToolInvocationRequest
            {
                Tools = new List<SingleToolInvocation>() // Empty list
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/tools/batch", batchRequest);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            
            var errorResponse = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            errorResponse.Should().NotBeNull();
            errorResponse!.Code.Should().Be("INVALID_BATCH_REQUEST");
        }

        [Fact]
        public async Task BatchInvokeTools_MixedResults_ReturnsAllResults()
        {
            // Arrange
            var batchRequest = new BatchToolInvocationRequest
            {
                Tools = new List<SingleToolInvocation>
                {
                    new()
                    {
                        ToolId = "file-system",
                        Request = new ToolInvocationRequest
                        {
                            Parameters = new Dictionary<string, object>
                            {
                                ["operation"] = "read",
                                ["path"] = "/valid/path"
                            }
                        }
                    },
                    new()
                    {
                        ToolId = "non-existent-tool",
                        Request = new ToolInvocationRequest
                        {
                            Parameters = new Dictionary<string, object>
                            {
                                ["param"] = "value"
                            }
                        }
                    }
                },
                StopOnError = false
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/tools/batch", batchRequest);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var batchResponse = await response.Content.ReadFromJsonAsync<IEnumerable<ToolInvocationResponse>>();
            batchResponse.Should().NotBeNull();
            batchResponse.Should().HaveCount(2);
            
            var results = batchResponse!.ToList();
            results[0].Status.Should().Be(ToolExecutionStatus.Success);
            results[1].Status.Should().Be(ToolExecutionStatus.Failed); // Non-existent tool
        }

        [Fact]
        public async Task InvokeTool_WithCustomTimeout_RespectsTimeout()
        {
            // Arrange
            var toolId = "code-executor";
            var request = new ToolInvocationRequest
            {
                Parameters = new Dictionary<string, object>
                {
                    ["language"] = "python",
                    ["code"] = "import time; time.sleep(0.1); print('Done')"
                },
                TimeoutSeconds = 1 // Short timeout but should be sufficient
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var toolResponse = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
            toolResponse.Should().NotBeNull();
            toolResponse!.Status.Should().Be(ToolExecutionStatus.Success);
        }

        [Fact]
        public async Task InvokeTool_ConcurrentRequests_HandlesLoadCorrectly()
        {
            // Arrange
            var toolId = "file-system";
            var tasks = new List<Task<HttpResponseMessage>>();
            
            for (int i = 0; i < 5; i++)
            {
                var request = new ToolInvocationRequest
                {
                    Parameters = new Dictionary<string, object>
                    {
                        ["operation"] = "list",
                        ["path"] = $"/tmp/test{i}"
                    }
                };
                
                tasks.Add(_client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request));
            }

            // Act
            var responses = await Task.WhenAll(tasks);

            // Assert
            responses.Should().HaveCount(5);
            responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
            
            foreach (var response in responses)
            {
                var toolResponse = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
                toolResponse.Should().NotBeNull();
                toolResponse!.Status.Should().Be(ToolExecutionStatus.Success);
                response.Dispose();
            }
        }

        [Fact]
        public async Task InvokeTool_LargeParameters_HandlesCorrectly()
        {
            // Arrange
            var toolId = "code-executor";
            var largeCode = string.Join("\n", Enumerable.Repeat("print('Large code test')", 100));
            
            var request = new ToolInvocationRequest
            {
                Parameters = new Dictionary<string, object>
                {
                    ["language"] = "python",
                    ["code"] = largeCode
                },
                Context = Enumerable.Range(1, 50).ToDictionary(i => $"context{i}", i => (object)$"value{i}")
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var toolResponse = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
            toolResponse.Should().NotBeNull();
            toolResponse!.Status.Should().Be(ToolExecutionStatus.Success);
        }

        [Fact]
        public async Task ApiEndpoints_HaveCorrectContentTypes()
        {
            // Act & Assert - Test various endpoints
            var endpoints = new[]
            {
                "/api/tools",
                "/api/tools/health",
                "/api/tools/file-system"
            };

            foreach (var endpoint in endpoints)
            {
                var response = await _client.GetAsync(endpoint);
                response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
            }
        }

        [Fact]
        public async Task InvokeTool_InvalidJsonInParameters_HandleGracefully()
        {
            // Arrange
            var toolId = "file-system";
            var request = new ToolInvocationRequest
            {
                Parameters = new Dictionary<string, object>
                {
                    ["operation"] = "read",
                    ["path"] = "/test/path",
                    ["invalidJsonObject"] = "{ this is not valid json }"
                }
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/tools/{toolId}/invoke", request);

            // Assert
            // Should still work since we're not parsing the parameter as JSON
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
} 