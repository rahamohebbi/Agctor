using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Linq;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Mcp
{
    /// <summary>
    /// Hosted service that implements a Model Context Protocol (MCP) listener.
    /// Accepts TCP connections and routes messages to agents following Actor Model principles.
    /// Supports the MCP standard for message format and protocol.
    /// </summary>
    public class McpListener : BackgroundService
    {
        private readonly IMessageDispatcher _messageDispatcher;
        private readonly ILogger<McpListener> _logger;
        private readonly IConfiguration _configuration;
        private readonly McpEndpointInfo _endpointInfo;
        private TcpListener? _tcpListener;
        private readonly List<McpClientConnection> _activeConnections = new();
        private readonly object _connectionLock = new();

        // Default configuration
        private const int DefaultPort = 8080;
        private const string DefaultHost = "0.0.0.0";

        public McpListener(
            IMessageDispatcher messageDispatcher,
            ILogger<McpListener> logger,
            IConfiguration configuration,
            McpEndpointInfo endpointInfo)
        {
            _messageDispatcher = messageDispatcher ?? throw new ArgumentNullException(nameof(messageDispatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
        }

        /// <summary>
        /// Starts the MCP listener and begins accepting client connections.
        /// Follows Actor Model isolation principles by handling each connection independently.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting MCP listener...");

            try
            {
                // Get configuration (log values for diagnostics)
                var port = _configuration.GetValue<int>("Mcp:Port", DefaultPort);
                var host = _configuration.GetValue<string>("Mcp:Host") ?? DefaultHost;

                _logger.LogDebug("MCP listener resolved configuration Host={Host} Port={Port}", host, port);

                // Start TCP listener
                var ipAddress = IPAddress.Parse(host);
                _tcpListener = new TcpListener(ipAddress, port);
                _tcpListener.Start();

                // Store the real bound endpoint so integration tests can discover it
                _endpointInfo.Host = host;
                _endpointInfo.Port = ((_tcpListener.LocalEndpoint as System.Net.IPEndPoint)?.Port) ?? port;

                _logger.LogInformation("MCP listener started on {Host}:{Port}", host, port);

                // Accept connections until cancellation is requested
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                        _logger.LogInformation("New MCP client connected from {RemoteEndpoint}", 
                            tcpClient.Client.RemoteEndPoint);

                        // Handle each client connection independently (Actor Model isolation)
                        var clientConnection = new McpClientConnection(tcpClient, _messageDispatcher, _logger);
                        lock (_connectionLock)
                        {
                            _activeConnections.Add(clientConnection);
                        }

                        // Start handling the client in the background
                        _ = Task.Run(async () => await HandleClientAsync(clientConnection, stoppingToken), stoppingToken);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected when shutting down
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error accepting MCP client connection");
                        await Task.Delay(1000, stoppingToken); // Brief delay before retrying
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in MCP listener");
            }
            finally
            {
                _tcpListener?.Stop();
                await CleanupConnectionsAsync();
                _logger.LogInformation("MCP listener stopped");
            }
        }

        /// <summary>
        /// Handles a single MCP client connection.
        /// Processes messages in isolation following Actor Model principles.
        /// </summary>
        private async Task HandleClientAsync(McpClientConnection clientConnection, CancellationToken cancellationToken)
        {
            try
            {
                await clientConnection.ProcessMessagesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling MCP client {ClientId}", clientConnection.Id);
            }
            finally
            {
                lock (_connectionLock)
                {
                    _activeConnections.Remove(clientConnection);
                }
                clientConnection.Dispose();
            }
        }

        /// <summary>
        /// Cleans up all active connections during shutdown.
        /// </summary>
        private async Task CleanupConnectionsAsync()
        {
            List<McpClientConnection> snapshot;
            lock (_connectionLock)
            {
                snapshot = _activeConnections.ToList();
                _activeConnections.Clear();
            }

            if (snapshot.Count == 0) return;

            var cleanupTasks = snapshot.Select(conn => Task.Run(() => conn.Dispose()));
            await Task.WhenAll(cleanupTasks);
        }

        /// <summary>
        /// Gracefully stops the MCP listener.
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping MCP listener...");
            _tcpListener?.Stop();
            await CleanupConnectionsAsync();
            await base.StopAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Represents a single MCP client connection.
    /// Handles message processing for one client in isolation (Actor Model principle).
    /// </summary>
    public class McpClientConnection : IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly IMessageDispatcher _messageDispatcher;
        private readonly ILogger _logger;
        private bool _disposed = false;

        public McpClientConnection(TcpClient tcpClient, IMessageDispatcher messageDispatcher, ILogger logger)
        {
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _messageDispatcher = messageDispatcher ?? throw new ArgumentNullException(nameof(messageDispatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stream = _tcpClient.GetStream();

            _logger.LogDebug("Created MCP client connection {ClientId}", Id);
        }

        /// <summary>
        /// Processes incoming messages from the MCP client.
        /// Each message is handled independently following Actor Model isolation.
        /// </summary>
        public async Task ProcessMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var messageBuffer = new StringBuilder();

            try
            {
                while (!cancellationToken.IsCancellationRequested && _tcpClient.Connected)
                {
                    var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                    {
                        _logger.LogInformation("MCP client {ClientId} disconnected", Id);
                        break;
                    }

                    var data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuffer.Append(data);

                    // Process complete messages (assuming newline-delimited JSON)
                    await ProcessCompleteMessages(messageBuffer, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing messages from MCP client {ClientId}", Id);
            }
        }

        /// <summary>
        /// Processes complete MCP messages from the buffer.
        /// Supports the Model Context Protocol message format.
        /// </summary>
        private async Task ProcessCompleteMessages(StringBuilder messageBuffer, CancellationToken cancellationToken)
        {
            // Continuously look for a newline character ("\n") which delimits the end of one MCP JSON message.
            // Everything before the newline is considered a complete message, anything after (without a newline) is kept
            // in the buffer until more data arrives. This avoids the off-by-one error we had previously where the last
            // legitimate message was skipped if the buffer ended with a newline.

            while (true)
            {
                var bufferString = messageBuffer.ToString();
                var newlineIndex = bufferString.IndexOf('\n');

                if (newlineIndex == -1)
                {
                    // No complete message yet
                    return;
                }

                // Extract the complete message (without the newline)
                var messageJson = bufferString.Substring(0, newlineIndex).Trim();

                // Remove the processed part from the buffer (including the newline character)
                messageBuffer.Remove(0, newlineIndex + 1);

                if (string.IsNullOrEmpty(messageJson))
                {
                    continue; // Ignore empty lines
                }

                try
                {
                    await ProcessSingleMessage(messageJson, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing MCP message from client {ClientId}: {Message}", Id, messageJson);
                    await SendErrorResponse($"Error processing message: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Processes a single MCP message and routes it to the appropriate agent.
        /// Converts MCP format to Actor Model message envelope.
        /// </summary>
        private async Task ProcessSingleMessage(string messageJson, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Processing MCP message from client {ClientId}: {Message}", Id, messageJson);

            // Parse MCP message
            var mcpMessage = JsonSerializer.Deserialize<McpMessage>(messageJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            if (mcpMessage == null)
            {
                await SendErrorResponse("Invalid message format");
                return;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(mcpMessage.TargetAgent))
            {
                await SendErrorResponse("TargetAgent is required");
                return;
            }

            if (mcpMessage.Payload == null)
            {
                await SendErrorResponse("Payload is required");
                return;
            }

            // Create message envelope from MCP message
            var envelope = CreateMessageEnvelope(mcpMessage);

            // Route message through dispatcher
            var response = await _messageDispatcher.SendMessageAsync(mcpMessage.TargetAgent, envelope, cancellationToken);

            // Send response back to client
            await SendResponse(response);
        }

        /// <summary>
        /// Creates a message envelope from an MCP message.
        /// Applies MCP conventions and Actor Model principles.
        /// </summary>
        private IMessageEnvelope CreateMessageEnvelope(McpMessage mcpMessage)
        {
            // Create headers with MCP context
            var headers = new Dictionary<string, string>
            {
                ["source"] = "mcp-client",
                ["client-id"] = Id,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["content-type"] = "application/json",
                ["mcp-version"] = "1.0"
            };

            // Add any additional headers from the MCP message
            if (mcpMessage.Headers != null)
            {
                foreach (var header in mcpMessage.Headers)
                {
                    headers[header.Key] = header.Value;
                }
            }

            // Create metadata with MCP context
            var metadata = new Dictionary<string, object>
            {
                ["source"] = "mcp-client",
                ["client-id"] = Id,
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["mcp-version"] = "1.0"
            };

            // Add any additional metadata from the MCP message
            if (mcpMessage.Metadata != null)
            {
                foreach (var meta in mcpMessage.Metadata)
                {
                    metadata[meta.Key] = meta.Value;
                }
            }

            return new MessageEnvelope(
                id: mcpMessage.Id ?? Guid.NewGuid().ToString(),
                payload: mcpMessage.Payload,
                metadata: metadata,
                headers: headers);
        }

        /// <summary>
        /// Sends a response back to the MCP client.
        /// </summary>
        private async Task SendResponse(object response)
        {
            try
            {
                var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                // Length-prefixed framing: 4-byte big-endian length followed by UTF-8 payload
                var payloadBytes = Encoding.UTF8.GetBytes(responseJson);
                byte[] lenBuf = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(lenBuf, payloadBytes.Length);
                await _stream.WriteAsync(lenBuf, 0, lenBuf.Length);
                await _stream.WriteAsync(payloadBytes, 0, payloadBytes.Length);
                await _stream.FlushAsync();

                _logger.LogDebug("Sent response to MCP client {ClientId}", Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending response to MCP client {ClientId}", Id);
            }
        }

        /// <summary>
        /// Sends an error response to the MCP client.
        /// </summary>
        private async Task SendErrorResponse(string errorMessage)
        {
            var errorResponse = new
            {
                Error = errorMessage,
                Timestamp = DateTimeOffset.UtcNow,
                ClientId = Id
            };

            await SendResponse(errorResponse);
        }

        /// <summary>
        /// Disposes of the client connection resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _stream?.Dispose();
                _tcpClient?.Close();
                _tcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MCP client connection {ClientId}", Id);
            }

            _disposed = true;
            _logger.LogDebug("Disposed MCP client connection {ClientId}", Id);
        }
    }

    /// <summary>
    /// Represents an MCP (Model Context Protocol) message.
    /// Defines the standard format for messages sent from MCP clients.
    /// </summary>
    public class McpMessage
    {
        /// <summary>
        /// Unique identifier for the message.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Target agent identifier where the message should be routed.
        /// </summary>
        public string TargetAgent { get; set; } = null!;

        /// <summary>
        /// Message payload/content.
        /// </summary>
        public object Payload { get; set; } = null!;

        /// <summary>
        /// Optional metadata dictionary following MCP conventions.
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Optional headers dictionary for protocol-level information.
        /// </summary>
        public Dictionary<string, string>? Headers { get; set; }

        /// <summary>
        /// Optional sender identifier.
        /// </summary>
        public string? Sender { get; set; }

        /// <summary>
        /// Message type for categorization.
        /// </summary>
        public string? MessageType { get; set; }
    }
} 