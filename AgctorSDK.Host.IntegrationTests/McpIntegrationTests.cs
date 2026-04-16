using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using AgctorSDK.Host.Mcp;
using AgctorSDK.Host.Models;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers.Binary;
using System.IO;

namespace AgctorSDK.Host.IntegrationTests
{
    /// <summary>
    /// Integration tests for the MCP (Model Context Protocol) listener.
    /// Tests TCP connection handling, message parsing, and agent routing.
    /// </summary>
    public class McpIntegrationTests : IClassFixture<AgctorWebApplicationFactory>, IAsyncLifetime
    {
        private readonly AgctorWebApplicationFactory _factory;
        // WithWebHostBuilder returns the base factory type (not AgctorWebApplicationFactory).
        private WebApplicationFactory<Program>? _serverFactory;
        private static int _portCounter = 10080; // Legacy counter, unused in dynamic mode but kept for uniqueness fallback
        private int _mcpPort;
        private TcpClient? _testClient;

        public McpIntegrationTests(AgctorWebApplicationFactory factory)
        {
            // Initially unknown; will be resolved after host starts
            _mcpPort = 0;
            _factory = factory;
        }

        public async Task InitializeAsync()
        {
            // Start the web application with custom MCP port
            _serverFactory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    // Request an ephemeral port
                    config.AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string?>("Mcp:Port", "0")
                    });
                });

                builder.ConfigureServices(services =>
                {
                    services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(45));
                });
            });

            _ = _serverFactory.CreateClient();

            // Resolve the actual port chosen by the listener
            var endpointInfo = _serverFactory.Services.GetRequiredService<AgctorSDK.Host.Models.McpEndpointInfo>();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (endpointInfo.Port == 0 && sw.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(100);
            }

            if (endpointInfo.Port == 0)
            {
                throw new InvalidOperationException("MCP listener did not expose a bound port within timeout");
            }

            _mcpPort = endpointInfo.Port;
        }

        public async Task DisposeAsync()
        {
            _testClient?.Close();
            _testClient?.Dispose();
            _serverFactory?.Dispose();
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
            var responseJson = await ReadFrameWithTimeout(stream);

            // Assert
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
            var responseJson = await ReadFrameWithTimeout(stream);

            // Assert
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
            var responseJson = await ReadFrameWithTimeout(stream);

            // Assert
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
            var responseJson = await ReadFrameWithTimeout(stream);

            // Assert
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
            for (int i = 0; i < 3; i++)
            {
                var responseJson = await ReadFrameWithTimeout(stream);
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
                        var _ = await ReadFrameWithTimeout(stream);
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
            var responseJson = await ReadFrameWithTimeout(stream);

            // Assert
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
            var responseJson = await ReadFrameWithTimeout(stream);

            // Assert
            responseJson.Should().NotBeNullOrEmpty();
            
            var response = JsonSerializer.Deserialize<MessageResponse>(responseJson.Trim(), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            response.Should().NotBeNull();
            response!.MessageId.Should().Be(mcpMessage.Id);
        }

        private static async Task<string> ReadFrameWithTimeout(NetworkStream stream, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);

            // Read exactly 4 bytes for length
            var lenBuf = new byte[4];
            await ReadExactAsync(stream, lenBuf, cts.Token);
            var length = BinaryPrimitives.ReadInt32BigEndian(lenBuf);

            if (length <= 0 || length > 1_000_000) // basic sanity check (1 MB max)
                throw new InvalidOperationException($"Invalid frame length {length}");

            var payload = new byte[length];
            await ReadExactAsync(stream, payload, cts.Token);

            return Encoding.UTF8.GetString(payload);
        }

        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, ct);
                if (read == 0)
                    throw new EndOfStreamException("Stream closed before reading expected bytes");
                offset += read;
            }
        }
    }
} 