using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services
{
    /// <summary>
    /// Interface for dispatching messages to agents through the Actor Model.
    /// Abstracts the message routing logic for both HTTP and MCP interfaces.
    /// </summary>
    public interface IMessageDispatcher
    {
        /// <summary>
        /// Sends a message to the specified agent and returns the result.
        /// </summary>
        /// <param name="agentId">Target agent identifier</param>
        /// <param name="request">Message request containing payload and metadata</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Message response with status and optional response data</returns>
        Task<MessageResponse> SendMessageAsync(string agentId, MessageRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a message envelope directly to an agent (used by MCP).
        /// </summary>
        /// <param name="agentId">Target agent identifier</param>
        /// <param name="envelope">Pre-constructed message envelope</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Message response with status and optional response data</returns>
        Task<MessageResponse> SendMessageAsync(string agentId, IMessageEnvelope envelope, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implementation of IMessageDispatcher that routes messages through the Actor Model.
    /// Leverages IActorRuntimeAdapter for agent communication and applies Actor Model principles.
    /// </summary>
    public class MessageDispatcher : IMessageDispatcher
    {
        private readonly IActorRuntimeAdapter _runtimeAdapter;
        private readonly IAgentRegistry _agentRegistry;
        private readonly ILogger<MessageDispatcher> _logger;

        public MessageDispatcher(
            IActorRuntimeAdapter runtimeAdapter,
            IAgentRegistry agentRegistry,
            ILogger<MessageDispatcher> logger)
        {
            _runtimeAdapter = runtimeAdapter ?? throw new ArgumentNullException(nameof(runtimeAdapter));
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Sends a message to the specified agent using HTTP request format.
        /// Converts the HTTP request to a message envelope and routes through Actor Model.
        /// </summary>
        public async Task<MessageResponse> SendMessageAsync(string agentId, MessageRequest request, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Dispatching message to agent {AgentId} from HTTP API", agentId);

            try
            {
                // Validate agent exists
                var agent = await _agentRegistry.GetAgentByIdAsync(agentId);
                if (agent == null)
                {
                    _logger.LogWarning("Agent {AgentId} not found in registry", agentId);
                    return new MessageResponse
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        Status = MessageStatus.AgentNotFound,
                        ErrorMessage = $"Agent '{agentId}' not found"
                    };
                }

                // Create message envelope from HTTP request
                var envelope = CreateMessageEnvelope(request);
                var senderId = request.SenderId ?? "http-api";

                // Send message through Actor Model
                var messageId = envelope.Id;
                await _runtimeAdapter.SendMessageAsync(
                    targetActorId: agentId,
                    message: envelope.Payload,
                    senderId: senderId,
                    headers: envelope.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Message {MessageId} successfully sent to agent {AgentId}", messageId, agentId);

                return new MessageResponse
                {
                    MessageId = messageId,
                    Status = MessageStatus.Success
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Message sending to agent {AgentId} was cancelled", agentId);
                return new MessageResponse
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Status = MessageStatus.Failed,
                    ErrorMessage = "Operation was cancelled"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to agent {AgentId}", agentId);
                return new MessageResponse
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Status = MessageStatus.Failed,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Sends a pre-constructed message envelope to an agent (primarily used by MCP).
        /// Follows Actor Model principles for message routing and isolation.
        /// </summary>
        public async Task<MessageResponse> SendMessageAsync(string agentId, IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Dispatching message envelope {MessageId} to agent {AgentId}", envelope.Id, agentId);

            try
            {
                // Validate agent exists
                var agent = await _agentRegistry.GetAgentByIdAsync(agentId);
                if (agent == null)
                {
                    _logger.LogWarning("Agent {AgentId} not found in registry", agentId);
                    return new MessageResponse
                    {
                        MessageId = envelope.Id,
                        Status = MessageStatus.AgentNotFound,
                        ErrorMessage = $"Agent '{agentId}' not found"
                    };
                }

                // Extract sender from headers or use default
                var senderId = envelope.Headers.TryGetValue("sender-id", out var senderIdValue) 
                    ? senderIdValue 
                    : "mcp-listener";

                // Send message through Actor Model runtime
                await _runtimeAdapter.SendMessageAsync(
                    targetActorId: agentId,
                    message: envelope.Payload,
                    senderId: senderId,
                    headers: envelope.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Message envelope {MessageId} successfully sent to agent {AgentId}", envelope.Id, agentId);

                return new MessageResponse
                {
                    MessageId = envelope.Id,
                    Status = MessageStatus.Success
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Message envelope sending to agent {AgentId} was cancelled", agentId);
                return new MessageResponse
                {
                    MessageId = envelope.Id,
                    Status = MessageStatus.Failed,
                    ErrorMessage = "Operation was cancelled"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message envelope to agent {AgentId}", agentId);
                return new MessageResponse
                {
                    MessageId = envelope.Id,
                    Status = MessageStatus.Failed,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Creates a message envelope from an HTTP request.
        /// Applies Model Context Protocol (MCP) conventions for metadata and headers.
        /// </summary>
        private IMessageEnvelope CreateMessageEnvelope(MessageRequest request)
        {
            // Create base headers with HTTP API context
            var headers = new Dictionary<string, string>
            {
                ["source"] = "http-api",
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["content-type"] = "application/json"
            };

            // Add any additional headers from the request
            if (request.Headers != null)
            {
                foreach (var header in request.Headers)
                {
                    headers[header.Key] = header.Value;
                }
            }

            // Create metadata with default values
            var metadata = new Dictionary<string, object>
            {
                ["source"] = "http-api",
                ["timestamp"] = DateTimeOffset.UtcNow
            };

            // Add any additional metadata from the request
            if (request.Metadata != null)
            {
                foreach (var meta in request.Metadata)
                {
                    metadata[meta.Key] = meta.Value;
                }
            }

            return new MessageEnvelope(
                id: Guid.NewGuid().ToString(),
                payload: request.Payload,
                metadata: metadata,
                headers: headers);
        }
    }
} 