using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Core.Streaming;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Host.Services.Traces;

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

        /// <summary>Same as <see cref="SendMessageAsync(string, MessageRequest, CancellationToken)"/> but adds <see cref="AgentStreamHeaders.StreamId"/> for SSE streaming (PRD-011).</summary>
        Task<MessageResponse> SendMessageAsync(string agentId, MessageRequest request, string? agentStreamId, CancellationToken cancellationToken = default);

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
        private const string SessionCoordinatorAgentId = "session-coordinator-agent";
        private readonly IActorRuntimeAdapter _runtimeAdapter;
        private readonly IAgentRegistry _agentRegistry;
        private readonly ISessionStore _sessionStore;
        private readonly ITraceTimelineStore _traceTimelineStore;
        private readonly IActivityTracker? _activityTracker;
        private readonly IAgentOutputStreamRegistry _streamRegistry;
        private readonly ICurrentScenarioStore _currentScenarioStore;
        private readonly IScenarioCatalog _scenarioCatalog;
        private readonly IScenarioFlowExecutionService _scenarioFlowExecution;
        private readonly ILogger<MessageDispatcher> _logger;

        public MessageDispatcher(
            IActorRuntimeAdapter runtimeAdapter,
            IAgentRegistry agentRegistry,
            ISessionStore sessionStore,
            ITraceTimelineStore traceTimelineStore,
            ICurrentScenarioStore currentScenarioStore,
            IScenarioCatalog scenarioCatalog,
            IScenarioFlowExecutionService scenarioFlowExecution,
            ILogger<MessageDispatcher> logger,
            IActivityTracker? activityTracker = null,
            IAgentOutputStreamRegistry? streamRegistry = null)
        {
            _runtimeAdapter = runtimeAdapter ?? throw new ArgumentNullException(nameof(runtimeAdapter));
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _traceTimelineStore = traceTimelineStore ?? throw new ArgumentNullException(nameof(traceTimelineStore));
            _currentScenarioStore = currentScenarioStore ?? throw new ArgumentNullException(nameof(currentScenarioStore));
            _scenarioCatalog = scenarioCatalog ?? throw new ArgumentNullException(nameof(scenarioCatalog));
            _scenarioFlowExecution = scenarioFlowExecution ?? throw new ArgumentNullException(nameof(scenarioFlowExecution));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _activityTracker = activityTracker;
            _streamRegistry = streamRegistry ?? NullAgentOutputStreamRegistry.Instance;
        }

        /// <inheritdoc />
        public Task<MessageResponse> SendMessageAsync(string agentId, MessageRequest request, CancellationToken cancellationToken = default) =>
            SendMessageAsync(agentId, request, agentStreamId: null, cancellationToken);

        /// <summary>
        /// Sends a message to the specified agent using HTTP request format.
        /// Converts the HTTP request to a message envelope and routes through Actor Model.
        /// </summary>
        public async Task<MessageResponse> SendMessageAsync(string agentId, MessageRequest request, string? agentStreamId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Dispatching message to agent {AgentId} from HTTP API", agentId);

            try
            {
                var sessionId = ExtractSessionId(request);
                var turnGroupId = !string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : null;
                // Route natural-language prompts from coder-agent to refactor-agent (which has LLM to convert to CodeEditorTool commands)
                var payloadStr = request.Payload is string s ? s : (request.Payload is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String ? je.GetString() : null);
                var senderId = request.SenderId ?? "http-api";
                var envelope = CreateMessageEnvelope(request, sessionId, agentStreamId);
                if (!string.IsNullOrWhiteSpace(agentStreamId))
                {
                    var tid = envelope.Headers.TryGetValue("trace-id", out var t) ? t : null;
                    _streamRegistry.Publish(agentStreamId, new AgentStreamEvent
                    {
                        Type = "phase",
                        Payload = $"Dispatching to {agentId}…",
                        TraceId = tid,
                        AgentId = agentId
                    });
                }
                var requestTraceId = envelope.Headers.TryGetValue("trace-id", out var liveTraceId) ? liveTraceId : null;
                SessionTurn? requestTurn = null;
                if (!string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(payloadStr))
                {
                    requestTurn = await TryAppendSessionTurnAsync(sessionId, SessionRole.User, payloadStr, senderId, turnGroupId, cancellationToken);
                }

                if (agentId == "coder-agent" && !string.IsNullOrWhiteSpace(payloadStr) &&
                    !payloadStr.TrimStart().StartsWith("CodeEditorTool", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Routing natural-language prompt from coder-agent to refactor-agent");
                    agentId = "refactor-agent";
                }

                // PRD-014 Phase 8.2: when the applied scenario defines a flow, answer via flow runner instead of the coordinator actor.
                if (string.Equals(agentId, SessionCoordinatorAgentId, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(payloadStr))
                {
                    var scn = _currentScenarioStore.GetCurrentScenarioName();
                    if (!string.IsNullOrWhiteSpace(scn))
                    {
                        var scenarioDef = _scenarioCatalog.Get(scn.Trim());
                        if (scenarioDef?.Flow != null)
                        {
                            _logger.LogInformation("Running scenario flow for {ScenarioId} (session-coordinator path)", scn);
                            var attachmentIds = ExtractAttachmentIdsFromRequest(request);
                            var fr = await _scenarioFlowExecution.RunAsync(
                                scn,
                                new ScenarioFlowRunRequest
                                {
                                    Message = payloadStr,
                                    SessionId = sessionId,
                                    AttachmentIds = attachmentIds.Count > 0 ? attachmentIds : null
                                },
                                cancellationToken).ConfigureAwait(false);

                            if (fr.Success && (fr.Output != null || fr.PendingPrompt != null))
                            {
                                var assistantText = fr.Completed
                                    ? fr.Output!
                                    : fr.PendingPrompt ?? fr.Output ?? "Waiting for your input.";

                                SessionTurn? flowResponseTurn = null;
                                if (!string.IsNullOrWhiteSpace(sessionId))
                                {
                                    flowResponseTurn = await TryAppendSessionTurnAsync(
                                        sessionId,
                                        SessionRole.Assistant,
                                        assistantText,
                                        agentId,
                                        turnGroupId,
                                        cancellationToken).ConfigureAwait(false);
                                }

                                var flowTraceId = envelope.Headers.TryGetValue("trace-id", out var ftid) ? ftid : ExtractTraceIdFromCurrentContext();
                                await TryCaptureTraceHistoryAsync(
                                    sessionId,
                                    turnGroupId,
                                    requestTurn,
                                    flowResponseTurn,
                                    flowTraceId,
                                    requestTraceId ?? flowTraceId,
                                    flowTraceId,
                                    agentId,
                                    cancellationToken).ConfigureAwait(false);

                                if (!string.IsNullOrWhiteSpace(agentStreamId))
                                {
                                    _streamRegistry.Publish(agentStreamId, new AgentStreamEvent
                                    {
                                        Type = "llm_delta",
                                        Payload = assistantText,
                                        TraceId = flowTraceId,
                                        AgentId = agentId
                                    });
                                }

                                return new MessageResponse
                                {
                                    MessageId = envelope.Id,
                                    Status = MessageStatus.Success,
                                    ResponseData = assistantText,
                                    TraceId = flowTraceId,
                                    ErrorMessage = null
                                };
                            }

                            await TryCaptureTraceHistoryAsync(
                                sessionId,
                                turnGroupId,
                                requestTurn,
                                responseTurn: null,
                                primaryTraceId: requestTraceId,
                                requestTraceId: requestTraceId,
                                responseTraceId: null,
                                agentId,
                                cancellationToken).ConfigureAwait(false);

                            return new MessageResponse
                            {
                                MessageId = Guid.NewGuid().ToString(),
                                Status = MessageStatus.Failed,
                                TraceId = requestTraceId,
                                ErrorMessage = fr.ErrorMessage ?? fr.ErrorCode ?? "Scenario flow failed."
                            };
                        }
                    }
                }

                // Validate agent exists
                var agent = await _agentRegistry.GetAgentByIdAsync(agentId);
                if (agent == null)
                {
                    _logger.LogWarning("Agent {AgentId} not found in registry", agentId);
                    await TryCaptureTraceHistoryAsync(
                        sessionId,
                        turnGroupId,
                        requestTurn,
                        responseTurn: null,
                        primaryTraceId: requestTraceId,
                        requestTraceId: requestTraceId,
                        responseTraceId: null,
                        agentId,
                        cancellationToken);
                    return new MessageResponse
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        Status = MessageStatus.AgentNotFound,
                        TraceId = requestTraceId,
                        ErrorMessage = $"Agent '{agentId}' not found"
                    };
                }

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
                SessionTurn? responseTurn = null;
                if (!string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(responseData))
                {
                    responseTurn = await TryAppendSessionTurnAsync(sessionId, SessionRole.Assistant, responseData, agentId, turnGroupId, cancellationToken);
                }

                var traceId = envelope.Headers.TryGetValue("trace-id", out var responseTraceId)
                    ? responseTraceId
                    : ExtractTraceIdFromCurrentContext();
                await TryCaptureTraceHistoryAsync(
                    sessionId,
                    turnGroupId,
                    requestTurn,
                    responseTurn,
                    traceId,
                    requestTraceId ?? traceId,
                    traceId,
                    agentId,
                    cancellationToken);

                return new MessageResponse
                {
                    MessageId = envelope.Id,
                    Status = isError ? MessageStatus.Failed : MessageStatus.Success,
                    ResponseData = responseData,
                    TraceId = traceId,
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
        private IMessageEnvelope CreateMessageEnvelope(MessageRequest request, string? sessionId, string? agentStreamId = null)
        {
            // Create base headers with HTTP API context
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = "http-api",
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["content-type"] = "application/json"
            };

            if (!string.IsNullOrWhiteSpace(agentStreamId))
            {
                headers[AgentStreamHeaders.StreamId] = agentStreamId.Trim();
            }

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
            object tentativePayload = request.Payload ?? string.Empty;
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
            object payload = request.Payload ?? string.Empty;
            if (request.Payload is System.Text.Json.JsonElement jsonElement)
            {
                payload = jsonElement.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => jsonElement.GetString()!,
                    System.Text.Json.JsonValueKind.Number when jsonElement.TryGetInt32(out var intValue) => intValue,
                    System.Text.Json.JsonValueKind.Number when jsonElement.TryGetDouble(out var doubleValue) => doubleValue,
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Null => string.Empty,
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

        private async Task<SessionTurn?> TryAppendSessionTurnAsync(string sessionId, SessionRole role, string content, string? actorId, string? turnGroupId, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                var existing = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
                if (existing == null)
                {
                    await _sessionStore.CreateSessionAsync(sessionId, null, projectId: null, cancellationToken);
                }

                var turn = new SessionTurn
                {
                    SessionId = sessionId,
                    TurnGroupId = string.IsNullOrWhiteSpace(turnGroupId) ? Guid.NewGuid().ToString() : turnGroupId,
                    Role = role,
                    Content = content,
                    AgentId = actorId
                };

                if (await _agentRegistry.GetAgentByIdAsync(SessionCoordinatorAgentId) != null)
                {
                    return await _runtimeAdapter.SendMessageAsync<SessionTurn>(
                        SessionCoordinatorAgentId,
                        new AgctorSDK.Core.Sessions.Messages.AppendSessionTurnMessage { Turn = turn },
                        timeout: TimeSpan.FromSeconds(20),
                        senderId: nameof(MessageDispatcher),
                        headers: new Dictionary<string, string> { ["MessageType"] = "SessionCommand" },
                        cancellationToken: cancellationToken);
                }

                return await _sessionStore.AppendTurnAsync(turn, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to append session turn for session {SessionId}", sessionId);
                return null;
            }
        }

        private string? ExtractTraceIdFromCurrentContext()
        {
            if (_activityTracker == null)
            {
                return null;
            }

            var context = _activityTracker.ExtractContext();
            return context.TryGetValue("trace-id", out var traceId) && !string.IsNullOrWhiteSpace(traceId)
                ? traceId
                : null;
        }

        private async Task TryCaptureTraceHistoryAsync(
            string? sessionId,
            string? turnGroupId,
            SessionTurn? requestTurn,
            SessionTurn? responseTurn,
            string? primaryTraceId,
            string? requestTraceId,
            string? responseTraceId,
            string? agentId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId) ||
                string.IsNullOrWhiteSpace(turnGroupId) ||
                requestTurn == null)
            {
                return;
            }

            try
            {
                var link = new SessionTraceLink
                {
                    SessionId = sessionId,
                    TurnGroupId = turnGroupId,
                    RequestTurnId = requestTurn.TurnId,
                    ResponseTurnId = responseTurn?.TurnId,
                    PrimaryTraceId = primaryTraceId,
                    RequestTraceId = requestTraceId,
                    ResponseTraceId = responseTraceId,
                    AgentId = agentId
                };

                if (await _agentRegistry.GetAgentByIdAsync(SessionCoordinatorAgentId) != null)
                {
                    await _runtimeAdapter.SendMessageAsync<SessionTraceLink>(
                        SessionCoordinatorAgentId,
                        new AgctorSDK.Core.Sessions.Messages.UpsertSessionTraceLinkMessage { TraceLink = link },
                        timeout: TimeSpan.FromSeconds(20),
                        senderId: nameof(MessageDispatcher),
                        headers: new Dictionary<string, string> { ["MessageType"] = "SessionCommand" },
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await _sessionStore.UpsertTraceLinkAsync(link, cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(primaryTraceId) || _activityTracker == null)
                {
                    return;
                }

                var activities = (await _activityTracker.GetTraceActivitiesAsync(primaryTraceId)).ToArray();
                if (activities.Length == 0)
                {
                    return;
                }

                var ordered = activities
                    .OrderBy(a => a.Timestamp)
                    .ThenBy(a => string.IsNullOrWhiteSpace(a.ParentId) ? 0 : 1)
                    .ToList();
                var start = ordered.Min(a => a.Timestamp);
                var end = ordered.Max(a => a.Timestamp.Add(a.Duration));
                var depthMap = BuildDepthMap(ordered);
                var timeline = new TraceTimelineResponse
                {
                    TraceId = primaryTraceId,
                    StartedAtUtc = start,
                    TotalDurationMs = Math.Max(1, (end - start).TotalMilliseconds),
                    Events = ordered
                        .Select((activity, index) => TraceTimelineEventMapper.Map(activity, index + 1, start, depthMap))
                        .ToList()
                };

                await _traceTimelineStore.SaveAsync(timeline, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture historical trace for session {SessionId}", sessionId);
            }
        }

        private static Dictionary<string, int> BuildDepthMap(IReadOnlyCollection<AgctorSDK.Core.Utils.Observability.Visualization.IActivity> activities)
        {
            var map = activities.ToDictionary(activity => activity.Id);
            var depth = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var activity in activities)
            {
                depth[activity.Id] = GetDepth(activity, map, depth);
            }

            return depth;
        }

        private static int GetDepth(
            AgctorSDK.Core.Utils.Observability.Visualization.IActivity activity,
            IReadOnlyDictionary<string, AgctorSDK.Core.Utils.Observability.Visualization.IActivity> activities,
            IDictionary<string, int> cache)
        {
            if (cache.TryGetValue(activity.Id, out var cached))
            {
                return cached;
            }

            if (string.IsNullOrWhiteSpace(activity.ParentId) || !activities.TryGetValue(activity.ParentId, out var parent))
            {
                return 0;
            }

            return GetDepth(parent, activities, cache) + 1;
        }

        /// <summary>PRD-024: optional attachment ids on coordinator flow runs (metadata.attachmentIds).</summary>
        private static List<string> ExtractAttachmentIdsFromRequest(MessageRequest request)
        {
            var ids = new List<string>();
            if (request.Metadata == null)
                return ids;

            if (!request.Metadata.TryGetValue("attachmentIds", out var raw) || raw == null)
                return ids;

            if (raw is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    var s = item.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(s))
                        ids.Add(s);
                }

                return ids;
            }

            if (raw is IEnumerable<object> objects)
            {
                foreach (var o in objects)
                {
                    var s = o?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(s))
                        ids.Add(s);
                }
            }

            return ids;
        }
    }
} 