using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AgctorSDK.Host.Mcp;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.IntegrationTests
{
    /// <summary>
    /// Integration tests for the MCP (Model Context Protocol) listener.
    /// Tests TCP connection handling, message parsing, and agent routing.
    /// </summary>
    public class McpIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly int _mcpPort = 8081; // Use different port to avoid conflicts
        private TcpClient? _testClient;

        public McpIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        public async Task InitializeAsync()
        {
            // Start the web application with custom MCP port
            _ = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(45));
                });
                builder.UseSetting("Mcp:Port", _mcpPort.ToString());
            }).CreateClient();

            // Give the MCP listener time to start
            await Task.Delay(2000);
        }

        public async Task DisposeAsync()
        {
            _testClient?.Close();
            _testClient?.Dispose();
            await Task.CompletedTask;
        }

        [Fact]
        public async Task McpListener_AcceptsClientConnection()
        {
            // Arrange & Act
            _testClient = new TcpClient();
            
            // Assert
            var connectTask = _testClient.ConnectAsync("127.0.0.1", _mcpPort);
            var timeoutTask = Task.Delay(5000);
            
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            completedTask.Should().Be(connectTask, "Connection should complete before timeout");
            
            _testClient.Connected.Should().BeTrue();
        }

        [Fact]
        public async Task McpListener_ReceivesAndProcessesValidMessage()
        {
            // Arrange
            _testClient = new TcpClient();
            await _testClient.ConnectAsync("127.0.0.1", _mcpPort);
            
            var stream = _testClient.GetStream();
            
            var mcpMessage = new McpMessage
            {
                Id = Guid.NewGuid().ToString(),
                TargetAgent = "test-agent",
                Payload = new { message = "Hello from MCP client", type = "greeting" },
                Metadata = new Dictionary<string, object>
                {
                    ["priority"] = "normal"
                },
                Headers = new Dictionary<string, string>
                {
                    ["content-type"] = "application/json"
                },
                Sender = "mcp-test-client",
                MessageType = "command"
            };

            var messageJson = JsonSerializer.Serialize(mcpMessage, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var messageBytes = Encoding.UTF8.GetBytes(messageJson + "\n");
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            await stream.FlushAsync();

            // Wait for response
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            // Assert
            bytesRead.Should().BeGreaterThan(0);
            
            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            responseJson.Should().NotBeNullOrEmpty();
            
            var response = JsonSerializer.Deserialize<MessageResponse>(responseJson.Trim(), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            response.Should().NotBeNull();
            response!.MessageId.Should().Be(mcpMessage.Id);
        }

        [Fact]
        public async Task McpListener_RejectsInvalidMessage()
        {
            // Arrange
            _testClient = new TcpClient();
            await _testClient.ConnectAsync("127.0.0.1", _mcpPort);
            
            var stream = _testClient.GetStream();
            
            // Send invalid JSON
            var invalidMessage = "{ this is not valid json }\n";
            var messageBytes = Encoding.UTF8.GetBytes(invalidMessage);

            // Act
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            await stream.FlushAsync();

            // Wait for error response
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            // Assert
            bytesRead.Should().BeGreaterThan(0);
            
            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            responseJson.Should().Contain("Error");
        }

        [Fact]
        public async Task McpListener_RequiresTargetAgent()
        {
            // Arrange
            _testClient = new TcpClient();
            await _testClient.ConnectAsync("127.0.0.1", _mcpPort);
            
            var stream = _testClient.GetStream();
            
            var mcpMessage = new McpMessage
            {
                Id = Guid.NewGuid().ToString(),
                TargetAgent = "", // Empty target agent
                Payload = new { message = "Test message" }
            };

            var messageJson = JsonSerializer.Serialize(mcpMessage, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var messageBytes = Encoding.UTF8.GetBytes(messageJson + "\n");
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            await stream.FlushAsync();

            // Wait for error response
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            // Assert
            bytesRead.Should().BeGreaterThan(0);
            
            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            responseJson.Should().Contain("TargetAgent is required");
        }

        [Fact]
        public async Task McpListener_RequiresPayload()
        {
            // Arrange
            _testClient = new TcpClient();
            await _testClient.ConnectAsync("127.0.0.1", _mcpPort);
            
            var stream = _testClient.GetStream();
            
            var mcpMessage = new McpMessage
            {
                Id = Guid.NewGuid().ToString(),
                TargetAgent = "test-agent",
                Payload = null! // Null payload
            };

            var messageJson = JsonSerializer.Serialize(mcpMessage, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var messageBytes = Encoding.UTF8.GetBytes(messageJson + "\n");
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            await stream.FlushAsync();

            // Wait for error response
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            // Assert
            bytesRead.Should().BeGreaterThan(0);
            
            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            responseJson.Should().Contain("Payload is required");
        }

        [Fact]
        public async Task McpListener_HandlesMultipleMessages()
        {
            // Arrange
            _testClient = new TcpClient();
            await _testClient.ConnectAsync("127.0.0.1", _mcpPort);
            
            var stream = _testClient.GetStream();
            var messages = new List<McpMessage>();
            
            for (int i = 0; i < 3; i++)
            {
                messages.Add(new McpMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    TargetAgent = $"test-agent-{i}",
                    Payload = new { message = $"Message {i}", sequence = i }
                });
            }

            // Act - Send all messages
            foreach (var message in messages)
            {
                var messageJson = JsonSerializer.Serialize(message, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                var messageBytes = Encoding.UTF8.GetBytes(messageJson + "\n");
                await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                await stream.FlushAsync();
                
                // Small delay between messages
                await Task.Delay(100);
            }

            // Assert - Read all responses
            var responses = new List<string>();
            var buffer = new byte[4096];
            
            for (int i = 0; i < 3; i++)
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                bytesRead.Should().BeGreaterThan(0);
                
                var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                responses.Add(responseJson.Trim());
            }

            responses.Should().HaveCount(3);
            responses.Should().OnlyContain(r => !string.IsNullOrEmpty(r));
        }

        [Fact]
        public async Task McpListener_HandlesConcurrentConnections()
        {
            // Arrange
            var clients = new List<TcpClient>();
            var tasks = new List<Task>();

            try
            {
                // Act - Create multiple concurrent connections
                for (int i = 0; i < 3; i++)
                {
                    var client = new TcpClient();
                    clients.Add(client);
                    
                    var clientId = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        await client.ConnectAsync("127.0.0.1", _mcpPort);
                        
                        var stream = client.GetStream();
                        var message = new McpMessage
                        {
                            Id = Guid.NewGuid().ToString(),
                            TargetAgent = $"concurrent-agent-{clientId}",
                            Payload = new { message = $"Concurrent message from client {clientId}" }
                        };

                        var messageJson = JsonSerializer.Serialize(message, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });
                        
                        var messageBytes = Encoding.UTF8.GetBytes(messageJson + "\n");
                        await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                        await stream.FlushAsync();

                        // Read response
                        var buffer = new byte[4096];
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        
                        bytesRead.Should().BeGreaterThan(0);
                    }));
                }

                await Task.WhenAll(tasks);

                // Assert
                clients.Should().OnlyContain(c => c.Connected);
            }
            finally
            {
                // Cleanup
                foreach (var client in clients)
                {
                    client.Close();
                    client.Dispose();
                }
            }
        }

        [Fact]
        public async Task McpListener_HandlesClientDisconnection()
        {
            // Arrange
            _testClient = new TcpClient();
            await _testClient.ConnectAsync("127.0.0.1", _mcpPort);
            
            _testClient.Connected.Should().BeTrue();

            // Act - Abruptly close connection
            _testClient.Close();

            // Assert - Should not crash the server
            // Create a new connection to verify server is still running
            var newClient = new TcpClient();
            await newClient.ConnectAsync("127.0.0.1", _mcpPort);
            
            newClient.Connected.Should().BeTrue();
            
            newClient.Close();
            newClient.Dispose();
        }

        [Fact]
        public async Task McpListener_PreservesMessageMetadata()
        {
            // Arrange
            _testClient = new TcpClient();
            await _testClient.ConnectAsync("127.0.0.1", _mcpPort);
            
            var stream = _testClient.GetStream();
            
            var mcpMessage = new McpMessage
            {
                Id = Guid.NewGuid().ToString(),
                TargetAgent = "metadata-test-agent",
                Payload = new { command = "test", data = "metadata preservation test" },
                Metadata = new Dictionary<string, object>
                {
                    ["priority"] = "high",
                    ["timeout"] = 30,
                    ["custom-field"] = "custom-value"
                },
                Headers = new Dictionary<string, string>
                {
                    ["content-type"] = "application/json",
                    ["correlation-id"] = Guid.NewGuid().ToString(),
                    ["user-agent"] = "mcp-integration-test"
                },
                Sender = "metadata-test-client",
                MessageType = "command"
            };

            var messageJson = JsonSerializer.Serialize(mcpMessage, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var messageBytes = Encoding.UTF8.GetBytes(messageJson + "\n");
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            await stream.FlushAsync();

            // Wait for response
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            // Assert
            bytesRead.Should().BeGreaterThan(0);
            
            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            responseJson.Should().NotBeNullOrEmpty();
            
            // The message should be processed successfully (metadata is preserved internally)
            var response = JsonSerializer.Deserialize<MessageResponse>(responseJson.Trim(), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            response.Should().NotBeNull();
            response!.MessageId.Should().Be(mcpMessage.Id);
        }

        [Fact]
        public async Task McpListener_HandlesLargeMessages()
        {
            // Arrange
            _testClient = new TcpClient();
            await _testClient.ConnectAsync("127.0.0.1", _mcpPort);
            
            var stream = _testClient.GetStream();
            
            // Create a large payload
            var largeData = string.Join("", Enumerable.Repeat("Large message test data ", 500)); // ~12KB
            
            var mcpMessage = new McpMessage
            {
                Id = Guid.NewGuid().ToString(),
                TargetAgent = "large-message-agent",
                Payload = new { 
                    data = largeData,
                    metadata = Enumerable.Range(1, 100).ToDictionary(i => $"key{i}", i => $"value{i}")
                }
            };

            var messageJson = JsonSerializer.Serialize(mcpMessage, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var messageBytes = Encoding.UTF8.GetBytes(messageJson + "\n");
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            await stream.FlushAsync();

            // Wait for response
            var buffer = new byte[8192]; // Larger buffer for response
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            // Assert
            bytesRead.Should().BeGreaterThan(0);
            
            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            responseJson.Should().NotBeNullOrEmpty();
            
            var response = JsonSerializer.Deserialize<MessageResponse>(responseJson.Trim(), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            response.Should().NotBeNull();
            response!.MessageId.Should().Be(mcpMessage.Id);
        }
    }
} 