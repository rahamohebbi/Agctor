using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Core.Utils.ActivityTracking;
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
        private readonly ISessionStore _sessionStore;
        private readonly IActivityTracker? _activityTracker;
        private readonly ILogger<MessageDispatcher> _logger;

        public MessageDispatcher(
            IActorRuntimeAdapter runtimeAdapter,
            IAgentRegistry agentRegistry,
            ISessionStore sessionStore,
            ILogger<MessageDispatcher> logger,
            IActivityTracker? activityTracker = null)
        {
            _runtimeAdapter = runtimeAdapter ?? throw new ArgumentNullException(nameof(runtimeAdapter));
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _activityTracker = activityTracker;
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
                var sessionId = ExtractSessionId(request);
                // Route natural-language prompts from coder-agent to refactor-agent (which has LLM to convert to CodeEditorTool commands)
                var payloadStr = request.Payload is string s ? s : (request.Payload is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String ? je.GetString() : null);
                var senderId = request.SenderId ?? "http-api";
                if (!string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(payloadStr))
                {
                    await TryAppendSessionTurnAsync(sessionId, SessionRole.User, payloadStr, senderId, cancellationToken);
                }

                if (agentId == "coder-agent" && !string.IsNullOrWhiteSpace(payloadStr) &&
                    !payloadStr.TrimStart().StartsWith("CodeEditorTool", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Routing natural-language prompt from coder-agent to refactor-agent");
                    agentId = "refactor-agent";
                }

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
                var envelope = CreateMessageEnvelope(request, sessionId);

                // Send message and wait for response (request-response pattern)
                var timeout = TimeSpan.FromSeconds(600); // Allow up to 10 minutes for complex refactor pipelines
                _logger.LogInformation("Sending request-response message to agent {AgentId} with {TimeoutSeconds}s timeout", agentId, timeout.TotalSeconds);
                
                var responseEnvelope = await _runtimeAdapter.SendMessageAsync<object>(
                    targetActorId: agentId,
                    message: envelope.Payload,
                    timeout: timeout,
                    senderId: senderId,
                    headers: envelope.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Received response from agent {AgentId}. Response: {ResponseType}", agentId, responseEnvelope?.GetType().Name ?? "null");

                // Convert non-string payloads to JSON for HTTP response
                string responseData;
                if (responseEnvelope is string str)
                {
                    responseData = str;
                }
                else if (responseEnvelope != null)
                {
                    responseData = System.Text.Json.JsonSerializer.Serialize(responseEnvelope);
                }
                else
                {
                    responseData = string.Empty;
                }

                var isError = false; // If we got a string response, it's successful (errors would throw exceptions)
                if (!string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(responseData))
                {
                    await TryAppendSessionTurnAsync(sessionId, SessionRole.Assistant, responseData, agentId, cancellationToken);
                }

                return new MessageResponse
                {
                    MessageId = envelope.Id,
                    Status = isError ? MessageStatus.Failed : MessageStatus.Success,
                    ResponseData = responseData,
                    TraceId = envelope.Headers.TryGetValue("trace-id", out var traceId) ? traceId : null,
                    ErrorMessage = isError ? responseData : null
                };
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Message sending to agent {AgentId} timed out", agentId);
                return new MessageResponse
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Status = MessageStatus.Failed,
                    ErrorMessage = "Agent response timed out"
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
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) && ex.Message.Contains("actor", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Agent {AgentId} not found in actor runtime: {Message}", agentId, ex.Message);
                return new MessageResponse
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Status = MessageStatus.AgentNotFound,
                    ErrorMessage = $"Agent '{agentId}' not found in actor runtime"
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
            catch (InvalidOperationException ex) when (ex.Message.Contains("Target actor") && ex.Message.Contains("not found"))
            {
                _logger.LogWarning("Agent {AgentId} not found in actor runtime: {Message}", agentId, ex.Message);
                return new MessageResponse
                {
                    MessageId = envelope.Id,
                    Status = MessageStatus.AgentNotFound,
                    ErrorMessage = $"Agent '{agentId}' not found in actor runtime"
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
        private IMessageEnvelope CreateMessageEnvelope(MessageRequest request, string? sessionId)
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
            if (!headers.ContainsKey("trace-id") && _activityTracker != null)
            {
                _activityTracker.PropagateContext(headers);
            }
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                headers["session-id"] = sessionId;
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
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                metadata["sessionId"] = sessionId;
            }

            // Ensure MessageType header exists; treat raw string payloads as Prompt.
            // We must base this on the final payload that will be placed in the envelope (may differ after JsonElement conversion).
            object tentativePayload = request.Payload;
            if (tentativePayload is System.Text.Json.JsonElement je)
            {
                tentativePayload = je.ValueKind == System.Text.Json.JsonValueKind.String ? je.GetString()! : tentativePayload;
            }
            if (!headers.ContainsKey("MessageType"))
            {
                headers["MessageType"] = tentativePayload is string ? "Prompt" : tentativePayload?.GetType().Name ?? "Unknown";
            }

            // Convert payload to appropriate type
            // If it's a JsonElement (from HTTP JSON deserialization), extract the actual value
            object payload = request.Payload;
            if (request.Payload is System.Text.Json.JsonElement jsonElement)
            {
                payload = jsonElement.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => jsonElement.GetString()!,
                    System.Text.Json.JsonValueKind.Number when jsonElement.TryGetInt32(out var intValue) => intValue,
                    System.Text.Json.JsonValueKind.Number when jsonElement.TryGetDouble(out var doubleValue) => doubleValue,
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Null => null!,
                    System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array => jsonElement.GetRawText(),
                    _ => jsonElement.ToString()
                };
            }

            return new MessageEnvelope(
                id: Guid.NewGuid().ToString(),
                payload: payload,
                metadata: metadata,
                headers: headers);
        }

        private static string? ExtractSessionId(MessageRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                return request.SessionId.Trim();
            }

            if (request.Headers != null)
            {
                if (request.Headers.TryGetValue("session-id", out var headerVal) && !string.IsNullOrWhiteSpace(headerVal))
                {
                    return headerVal.Trim();
                }
                if (request.Headers.TryGetValue("SessionId", out var altHeader) && !string.IsNullOrWhiteSpace(altHeader))
                {
                    return altHeader.Trim();
                }
            }

            if (request.Metadata != null)
            {
                if (request.Metadata.TryGetValue("sessionId", out var metaVal) && metaVal != null && !string.IsNullOrWhiteSpace(metaVal.ToString()))
                {
                    return metaVal.ToString()!.Trim();
                }
                if (request.Metadata.TryGetValue("session-id", out var altMetaVal) && altMetaVal != null && !string.IsNullOrWhiteSpace(altMetaVal.ToString()))
                {
                    return altMetaVal.ToString()!.Trim();
                }
            }

            return null;
        }

        private async Task TryAppendSessionTurnAsync(string sessionId, SessionRole role, string content, string? actorId, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(content))
                {
                    return;
                }

                var existing = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
                if (existing == null)
                {
                    await _sessionStore.CreateSessionAsync(sessionId, null, cancellationToken);
                }

                await _sessionStore.AppendTurnAsync(new SessionTurn
                {
                    SessionId = sessionId,
                    Role = role,
                    Content = content,
                    AgentId = actorId
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to append session turn for session {SessionId}", sessionId);
            }
        }
    }
} 