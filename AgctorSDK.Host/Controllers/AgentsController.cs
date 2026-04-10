using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Streaming;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using AgctorSDK.Core.Utils.ActivityTracking;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Controllers
{
    /// <summary>
    /// Controller for agent-related operations including message routing and discovery.
    /// Provides RESTful endpoints for interacting with agents in the AGCTOR framework.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AgentsController : ControllerBase
    {
        private readonly IMessageDispatcher _messageDispatcher;
        private readonly IAgentRegistry _agentRegistry;
        private readonly IAgentFactory _agentFactory;
        private readonly IAgentDetailProviderRegistry _detailProviderRegistry;
        private readonly IAgentTypeEnablementService _agentTypeEnablement;
        private readonly AgentTypeOptions _agentTypeOptions;
        private readonly IAgentOutputStreamRegistry _streamRegistry;
        private readonly ILogger<AgentsController> _logger;
        private readonly IActivityTracker? _activityTracker;

        public AgentsController(
            IMessageDispatcher messageDispatcher,
            IAgentRegistry agentRegistry,
            IAgentFactory agentFactory,
            IAgentDetailProviderRegistry detailProviderRegistry,
            IAgentTypeEnablementService agentTypeEnablement,
            IOptions<AgentTypeOptions> agentTypeOptions,
            IAgentOutputStreamRegistry streamRegistry,
            ILogger<AgentsController> logger,
            IActivityTracker? activityTracker = null)
        {
            _messageDispatcher = messageDispatcher ?? throw new ArgumentNullException(nameof(messageDispatcher));
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
            _detailProviderRegistry = detailProviderRegistry ?? throw new ArgumentNullException(nameof(detailProviderRegistry));
            _agentTypeEnablement = agentTypeEnablement ?? throw new ArgumentNullException(nameof(agentTypeEnablement));
            _agentTypeOptions = agentTypeOptions?.Value ?? throw new ArgumentNullException(nameof(agentTypeOptions));
            _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _activityTracker = activityTracker;
        }

        /// <summary>
        /// Sends a message to a specific agent.
        /// </summary>
        /// <param name="agentId">The unique identifier of the target agent</param>
        /// <param name="request">The message request containing payload and metadata</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Response indicating message delivery status</returns>
        /// <response code="200">Message was successfully sent to the agent</response>
        /// <response code="400">Invalid request format or parameters</response>
        /// <response code="404">Agent not found</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpPost("{agentId}/message")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<MessageResponse>> SendMessageAsync(
            [FromRoute] string agentId,
            [FromBody] MessageRequest request,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Received message request for agent {AgentId}", agentId);
            using var requestActivity = _activityTracker?.StartActivity("http.agents.send-message");

            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(agentId))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_AGENT_ID",
                        Message = "Agent ID cannot be null or empty"
                    });
                }

                if (request?.Payload == null)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_PAYLOAD",
                        Message = "Message payload is required"
                    });
                }

                // Send message through dispatcher
                var response = await _messageDispatcher.SendMessageAsync(agentId, request, cancellationToken);

                // Attach a trace id for timeline visualization when dispatcher did not provide one.
                if (_activityTracker != null)
                {
                    if (string.IsNullOrWhiteSpace(response.TraceId))
                    {
                        var ctx = _activityTracker.ExtractContext();
                        if (ctx.TryGetValue("trace-id", out var traceId) && !string.IsNullOrWhiteSpace(traceId))
                        {
                            response.TraceId = traceId;
                        }
                    }
                }
                requestActivity?.SetStatus(ActivityStatus.Ok);

                // Return appropriate HTTP status based on message status
                return response.Status switch
                {
                    MessageStatus.Success => Ok(response),
                    MessageStatus.AgentNotFound => NotFound(new ErrorResponse
                    {
                        Code = "AGENT_NOT_FOUND",
                        Message = response.ErrorMessage ?? "Agent not found"
                    }),
                    MessageStatus.Failed => StatusCode(500, new ErrorResponse
                    {
                        Code = "MESSAGE_FAILED",
                        Message = response.ErrorMessage ?? "Message sending failed"
                    }),
                    MessageStatus.Processing => Accepted(response), // 202 Accepted for async processing
                    _ => StatusCode(500, new ErrorResponse
                    {
                        Code = "UNKNOWN_STATUS",
                        Message = "Unknown message status"
                    })
                };
            }
            catch (Exception ex)
            {
                requestActivity?.RecordException(ex);
                requestActivity?.SetStatus(ActivityStatus.Error, ex.Message);
                _logger.LogError(ex, "Error sending message to agent {AgentId}", agentId);
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred while processing the message"
                });
            }
        }

        /// <summary>
        /// Sends a message and streams LLM/agent progress as SSE (PRD-011). Same body as <see cref="SendMessageAsync"/>.
        /// </summary>
        [HttpPost("{agentId}/message/stream")]
        [Produces("text/event-stream")]
        public async Task SendMessageStreamAsync(
            [FromRoute] string agentId,
            [FromBody] MessageRequest request,
            CancellationToken cancellationToken = default)
        {
            using var requestActivity = _activityTracker?.StartActivity("http.agents.send-message-stream");

            if (string.IsNullOrWhiteSpace(agentId))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (request?.Payload == null)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/event-stream; charset=utf-8";
            Response.Headers.CacheControl = "no-cache, no-transform";
            Response.Headers.Append("X-Accel-Buffering", "no");

            var streamId = Guid.NewGuid().ToString("N");
            var channel = Channel.CreateUnbounded<AgentStreamEvent>();
            var writer = channel.Writer;
            using var _reg = _streamRegistry.Register(streamId, writer);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, HttpContext.RequestAborted);
            var ct = linkedCts.Token;

            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            async Task WriteEventAsync(AgentStreamEvent evt)
            {
                var json = JsonSerializer.Serialize(evt, jsonOpts);
                await Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
                await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }

            MessageResponse response;
            try
            {
                var dispatchTask = _messageDispatcher.SendMessageAsync(agentId, request, streamId, ct);
                while (!dispatchTask.IsCompleted)
                {
                    var waitRead = channel.Reader.WaitToReadAsync(ct).AsTask();
                    await Task.WhenAny(dispatchTask, waitRead).ConfigureAwait(false);
                    while (channel.Reader.TryRead(out var evt))
                    {
                        await WriteEventAsync(evt).ConfigureAwait(false);
                    }
                }

                response = await dispatchTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Streaming message failed for agent {AgentId}", agentId);
                response = new MessageResponse
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Status = MessageStatus.Failed,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                writer.TryComplete();
            }

            while (channel.Reader.TryRead(out var leftover))
            {
                await WriteEventAsync(leftover).ConfigureAwait(false);
            }

            if (_activityTracker != null && string.IsNullOrWhiteSpace(response.TraceId))
            {
                var ctx = _activityTracker.ExtractContext();
                if (ctx.TryGetValue("trace-id", out var traceId) && !string.IsNullOrWhiteSpace(traceId))
                {
                    response.TraceId = traceId;
                }
            }

            var donePayload = new
            {
                type = "done",
                status = response.Status.ToString(),
                responseData = response.ResponseData,
                traceId = response.TraceId,
                errorMessage = response.ErrorMessage,
                messageId = response.MessageId
            };
            await Response.WriteAsync("data: " + JsonSerializer.Serialize(donePayload, jsonOpts) + "\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            requestActivity?.SetStatus(ActivityStatus.Ok);
        }

        /// <summary>
        /// Gets a list of all currently registered agents.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Collection of agent information</returns>
        /// <response code="200">Successfully retrieved agent list</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<AgentInfo>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<AgentInfo>>> GetAgentsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving list of all agents");

            try
            {
                var agents = await _agentRegistry.GetAllAgentsAsync();
                
                var agentInfos = agents.Select(agent => new AgentInfo
                {
                    Id = agent.Id,
                    Type = agent.GetType().Name,
                    Status = Models.AgentStatus.Active, // Assume active if in registry
                    Metadata = new Dictionary<string, object>
                    {
                        ["capabilities"] = GetAgentCapabilities(agent),
                        ["created"] = DateTimeOffset.UtcNow // Placeholder - would come from agent metadata
                    }
                }).ToList();

                _logger.LogInformation("Retrieved {AgentCount} agents", agentInfos.Count);
                return Ok(agentInfos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving agents list");
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred while retrieving agents"
                });
            }
        }

        /// <summary>
        /// Persists enable/disable for a registered agent type and stops running instances when disabled (PRD-010).
        /// </summary>
        [HttpPut("types/{typeName}/enabled")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> SetAgentTypeEnabledAsync(
            [FromRoute] string typeName,
            [FromBody] AgentTypeEnableRequest? request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = "INVALID_BODY",
                    Message = "Request body with enabled flag is required."
                });
            }

            try
            {
                await _agentTypeEnablement.SetTypeEnabledAsync(typeName, request.Enabled, cancellationToken).ConfigureAwait(false);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = "INVALID_AGENT_TYPE",
                    Message = ex.Message
                });
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to persist agent type enablement for {TypeName}", typeName);
                return StatusCode(500, new ErrorResponse
                {
                    Code = "SETTINGS_IO_ERROR",
                    Message = "Could not write user settings file. Check host permissions for appsettings.User.json."
                });
            }
        }

        /// <summary>
        /// Gets information about a specific agent.
        /// </summary>
        /// <param name="agentId">The unique identifier of the agent</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Agent information</returns>
        /// <response code="200">Successfully retrieved agent information</response>
        /// <response code="404">Agent not found</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpGet("{agentId}")]
        [ProducesResponseType(typeof(AgentInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AgentInfo>> GetAgentAsync(
            [FromRoute] string agentId,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving information for agent {AgentId}", agentId);

            try
            {
                if (string.IsNullOrWhiteSpace(agentId))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_AGENT_ID",
                        Message = "Agent ID cannot be null or empty"
                    });
                }

                var agent = await _agentRegistry.GetAgentByIdAsync(agentId);
                if (agent == null)
                {
                    _logger.LogWarning("Agent {AgentId} not found", agentId);
                    return NotFound(new ErrorResponse
                    {
                        Code = "AGENT_NOT_FOUND",
                        Message = $"Agent '{agentId}' not found"
                    });
                }

                var agentInfo = new AgentInfo
                {
                    Id = agent.Id,
                    Type = agent.GetType().Name,
                    Status = Models.AgentStatus.Active, // Assume active if in registry
                    Metadata = new Dictionary<string, object>
                    {
                        ["capabilities"] = GetAgentCapabilities(agent),
                        ["created"] = DateTimeOffset.UtcNow // Placeholder - would come from agent metadata
                    }
                };

                _logger.LogInformation("Retrieved information for agent {AgentId}", agentId);
                return Ok(agentInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving agent {AgentId}", agentId);
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred while retrieving agent information"
                });
            }
        }

        /// <summary>
        /// Gets agent info plus type-specific detail for the dashboard (PRD-006).
        /// </summary>
        [HttpGet("{agentId}/detail")]
        [ProducesResponseType(typeof(AgentDetailResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AgentDetailResponse>> GetAgentDetailAsync(
            [FromRoute] string agentId,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving detail for agent {AgentId}", agentId);
            if (string.IsNullOrWhiteSpace(agentId))
            {
                return BadRequest(new ErrorResponse { Code = "INVALID_AGENT_ID", Message = "Agent ID cannot be null or empty" });
            }
            try
            {
                var agent = await _agentRegistry.GetAgentByIdAsync(agentId);
                if (agent == null)
                {
                    return NotFound(new ErrorResponse { Code = "AGENT_NOT_FOUND", Message = $"Agent '{agentId}' not found" });
                }
                var detail = _detailProviderRegistry.GetDetail(agent);
                var response = new AgentDetailResponse
                {
                    Id = agent.Id,
                    Type = agent.GetType().Name,
                    Status = Models.AgentStatus.Active,
                    Metadata = new Dictionary<string, object> { ["capabilities"] = GetAgentCapabilities(agent) },
                    LastUpdated = DateTimeOffset.UtcNow,
                    Detail = detail
                };
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving agent detail {AgentId}", agentId);
                return StatusCode(500, new ErrorResponse { Code = "INTERNAL_ERROR", Message = "An internal error occurred while retrieving agent detail" });
            }
        }

        /// <summary>
        /// Creates a new agent in the system.
        /// </summary>
        /// <param name="request">The agent creation request</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Response indicating agent creation status</returns>
        /// <response code="201">Agent was successfully created</response>
        /// <response code="400">Invalid request format or parameters</response>
        /// <response code="409">Agent with the same ID already exists</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpPost]
        [ProducesResponseType(typeof(AgentCreationResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AgentCreationResponse>> CreateAgentAsync(
            [FromBody] AgentCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating agent {AgentId} of type {AgentType}", request.AgentId, request.AgentType);

            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.AgentId))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_AGENT_ID",
                        Message = "Agent ID cannot be null or empty"
                    });
                }

                if (string.IsNullOrWhiteSpace(request.AgentType))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_AGENT_TYPE",
                        Message = "Agent type cannot be null or empty"
                    });
                }

                // Check if agent already exists
                var existingAgent = await _agentRegistry.GetAgentByIdAsync(request.AgentId);
                if (existingAgent != null)
                {
                    return Conflict(new ErrorResponse
                    {
                        Code = "AGENT_ALREADY_EXISTS",
                        Message = $"Agent with ID '{request.AgentId}' already exists"
                    });
                }

                // Create the agent
                var agent = await CreateAgentByTypeAsync(request.AgentId, request.AgentType, request.Configuration);
                
                // Register the agent
                await _agentRegistry.RegisterAgentAsync(agent);

                _logger.LogInformation("Successfully created agent {AgentId} of type {AgentType}", request.AgentId, request.AgentType);

                var response = new AgentCreationResponse(
                    Success: true,
                    AgentId: request.AgentId,
                    AgentType: request.AgentType,
                    ErrorMessage: null
                );

                return Created($"/api/agents/{request.AgentId}", response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid agent type {AgentType}", request.AgentType);
                return BadRequest(new ErrorResponse
                {
                    Code = "INVALID_AGENT_TYPE",
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating agent {AgentId}", request.AgentId);
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred while creating the agent"
                });
            }
        }

        /// <summary>
        /// Gets the health status of the agent system.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>System health information</returns>
        /// <response code="200">System is healthy</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpGet("health")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Health check requested");

            try
            {
                var agents = await _agentRegistry.GetAllAgentsAsync();
                var agentCount = agents.Count();

                var healthInfo = new
                {
                    status = "healthy",
                    timestamp = DateTimeOffset.UtcNow,
                    agents = new
                    {
                        total = agentCount,
                        active = agentCount // Simplified - assume all registered agents are active
                    },
                    version = "1.0.0"
                };

                return Ok(healthInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health check");
                return StatusCode(500, new ErrorResponse
                {
                    Code = "HEALTH_CHECK_FAILED",
                    Message = "Health check failed"
                });
            }
        }

        /// <summary>
        /// Helper method to create agents by type.
        /// </summary>
        /// <param name="agentId">The unique ID for the agent</param>
        /// <param name="agentType">The type of agent to create</param>
        /// <param name="configuration">Optional configuration for the agent</param>
        /// <returns>The created agent instance</returns>
        private async Task<IAgent> CreateAgentByTypeAsync(string agentId, string agentType, Dictionary<string, object>? configuration)
        {
            // Extract prompt from configuration or use default
            var prompt = "Default agent prompt";
            string? parentAgentId = null;

            if (configuration != null)
            {
                if (configuration.TryGetValue("prompt", out var promptValue) && promptValue is string promptStr)
                {
                    prompt = promptStr;
                }
                if (configuration.TryGetValue("parentAgentId", out var parentValue) && parentValue is string parentStr)
                {
                    parentAgentId = parentStr;
                }
            }

            // Create the appropriate agent based on type using SpawnAgentAsync
            IAgent agent = agentType switch
            {
                "RootAgent" => await _agentFactory.SpawnAgentAsync<Agent>(prompt, parentAgentId, agentId),
                "Agent" => await _agentFactory.SpawnAgentAsync<Agent>(prompt, parentAgentId, agentId),
                "LLMAgent" => await _agentFactory.SpawnAgentAsync<LLMAgent>(prompt, parentAgentId, agentId),
                "CodeExecutorTool" => await _agentFactory.SpawnAgentAsync<CodeExecutorTool>(prompt, parentAgentId, agentId),
                "CodeEditorTool" => await _agentFactory.SpawnAgentAsync<CodeEditorTool>(prompt, parentAgentId, agentId),
                _ => throw new ArgumentException($"Unknown agent type: {agentType}")
            };

            // Apply additional configuration if provided
            if (configuration != null && configuration.Count > 0)
            {
                // In a full implementation, you'd apply agent-specific configuration
                _logger.LogInformation("Configuration provided for agent {AgentId}: {Configuration}", 
                    agentId, string.Join(", ", configuration.Keys));
            }

            return agent;
        }

        /// <summary>
        /// Helper method to extract agent capabilities.
        /// In a real implementation, this would inspect the agent's actual capabilities.
        /// </summary>
        /// <param name="agent">The agent instance</param>
        /// <returns>List of capabilities</returns>
        private List<string> GetAgentCapabilities(IAgent agent)
        {
            // This is a simplified implementation
            // In reality, you'd inspect the agent's interfaces, tools, etc.
            var capabilities = new List<string> { "message-processing" };
            
            // Example: check if agent implements specific interfaces
            var agentType = agent.GetType();
            if (agentType.GetInterfaces().Any(i => i.Name.Contains("Tool")))
            {
                capabilities.Add("tool-usage");
            }

            return capabilities;
        }
    }
} 