using Microsoft.AspNetCore.Mvc;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;

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
        private readonly ILogger<AgentsController> _logger;

        public AgentsController(
            IMessageDispatcher messageDispatcher,
            IAgentRegistry agentRegistry,
            ILogger<AgentsController> logger)
        {
            _messageDispatcher = messageDispatcher ?? throw new ArgumentNullException(nameof(messageDispatcher));
            _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                _logger.LogError(ex, "Error sending message to agent {AgentId}", agentId);
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred while processing the message"
                });
            }
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