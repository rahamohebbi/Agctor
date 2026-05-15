using Microsoft.AspNetCore.Mvc;
using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.Controllers
{
    /// <summary>
    /// Controller for tool-related operations including direct tool invocation and discovery.
    /// Provides RESTful endpoints for executing tools without agent wrapper.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ToolsController : ControllerBase
    {
        private readonly IToolInvoker _toolInvoker;
        private readonly IToolAgentsInsightService _toolAgentsInsight;
        private readonly ILogger<ToolsController> _logger;

        public ToolsController(
            IToolInvoker toolInvoker,
            IToolAgentsInsightService toolAgentsInsight,
            ILogger<ToolsController> logger)
        {
            _toolInvoker = toolInvoker ?? throw new ArgumentNullException(nameof(toolInvoker));
            _toolAgentsInsight = toolAgentsInsight ?? throw new ArgumentNullException(nameof(toolAgentsInsight));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Invokes a tool with the specified parameters.
        /// </summary>
        /// <param name="toolId">The unique identifier of the tool to invoke</param>
        /// <param name="request">The tool invocation request containing parameters and context</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Response containing tool execution result</returns>
        /// <response code="200">Tool was successfully executed</response>
        /// <response code="400">Invalid request format or parameters</response>
        /// <response code="404">Tool not found</response>
        /// <response code="408">Tool execution timed out</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpPost("{toolId}/invoke")]
        [ProducesResponseType(typeof(ToolInvocationResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status408RequestTimeout)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ToolInvocationResponse>> InvokeToolAsync(
            [FromRoute] string toolId,
            [FromBody] ToolInvocationRequest request,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Received tool invocation request for tool {ToolId}", toolId);

            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(toolId))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_TOOL_ID",
                        Message = "Tool ID cannot be null or empty"
                    });
                }

                if (request == null)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_PARAMETERS",
                        Message = "Tool parameters are required"
                    });
                }

                if (request.Parameters == null || request.Parameters.Count == 0)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_PARAMETERS",
                        Message = "Tool parameters are required"
                    });
                }

                // Invoke tool through the invoker service
                var response = await _toolInvoker.InvokeToolAsync(toolId, request, cancellationToken);

                // Return appropriate HTTP status based on tool execution status
                return response.Status switch
                {
                    ToolExecutionStatus.Success => Ok(response),
                    ToolExecutionStatus.ToolNotFound => NotFound(new ErrorResponse
                    {
                        Code = "TOOL_NOT_FOUND",
                        Message = response.ErrorMessage ?? "Tool not found"
                    }),
                    ToolExecutionStatus.InvalidParameters => BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_PARAMETERS",
                        Message = response.ErrorMessage ?? "Invalid parameters provided"
                    }),
                    ToolExecutionStatus.Timeout => StatusCode(408, new ErrorResponse
                    {
                        Code = "EXECUTION_TIMEOUT",
                        Message = response.ErrorMessage ?? "Tool execution timed out"
                    }),
                    ToolExecutionStatus.Failed => StatusCode(500, new ErrorResponse
                    {
                        Code = "EXECUTION_FAILED",
                        Message = response.ErrorMessage ?? "Tool execution failed"
                    }),
                    _ => StatusCode(500, new ErrorResponse
                    {
                        Code = "UNKNOWN_STATUS",
                        Message = "Unknown execution status"
                    })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking tool {ToolId}", toolId);
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred while invoking the tool"
                });
            }
        }

        /// <summary>
        /// Dashboard: registered host tools plus agents linked via project-memory <c>tools.allow</c> and known C# routing patterns.
        /// </summary>
        [HttpGet("agent-associations")]
        [ProducesResponseType(typeof(ToolAgentsInsightResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ToolAgentsInsightResponse>> GetToolAgentAssociationsAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var dto = await _toolAgentsInsight.GetInsightAsync(cancellationToken).ConfigureAwait(false);
                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building tool/agent associations");
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred while building tool associations"
                });
            }
        }

        /// <summary>
        /// Gets a list of all available tools.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Collection of available tool identifiers</returns>
        /// <response code="200">Successfully retrieved tools list</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<string>>> GetToolsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving list of available tools");

            try
            {
                var tools = await _toolInvoker.GetAvailableToolsAsync(cancellationToken);
                
                _logger.LogInformation("Retrieved {ToolCount} available tools", tools.Count());
                return Ok(tools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tools list");
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred while retrieving tools"
                });
            }
        }

        /// <summary>
        /// Gets information about a specific tool including its parameters.
        /// </summary>
        /// <param name="toolId">The unique identifier of the tool</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Tool information</returns>
        /// <response code="200">Successfully retrieved tool information</response>
        /// <response code="404">Tool not found</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpGet("{toolId}")]
        [ProducesResponseType(typeof(ToolInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ToolInfo>> GetToolInfoAsync(
            [FromRoute] string toolId,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving information for tool {ToolId}", toolId);

            try
            {
                if (string.IsNullOrWhiteSpace(toolId))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_TOOL_ID",
                        Message = "Tool ID cannot be null or empty"
                    });
                }

                var toolInfo = await _toolInvoker.GetToolInfoAsync(toolId, cancellationToken);
                if (toolInfo == null)
                {
                    _logger.LogWarning("Tool {ToolId} not found", toolId);
                    return NotFound(new ErrorResponse
                    {
                        Code = "TOOL_NOT_FOUND",
                        Message = $"Tool '{toolId}' not found"
                    });
                }

                _logger.LogInformation("Retrieved information for tool {ToolId}", toolId);
                return Ok(toolInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tool {ToolId}", toolId);
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred while retrieving tool information"
                });
            }
        }

        /// <summary>
        /// Gets the health status of the tool system.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Tool system health information</returns>
        /// <response code="200">Tool system is healthy</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpGet("health")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Tool system health check requested");

            try
            {
                var tools = await _toolInvoker.GetAvailableToolsAsync(cancellationToken);
                var toolCount = tools.Count();

                var healthInfo = new
                {
                    status = "healthy",
                    timestamp = DateTimeOffset.UtcNow,
                    tools = new
                    {
                        total = toolCount,
                        available = toolCount // Simplified - assume all tools are available
                    },
                    version = "1.0.0"
                };

                return Ok(healthInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during tool system health check");
                return StatusCode(500, new ErrorResponse
                {
                    Code = "HEALTH_CHECK_FAILED",
                    Message = "Tool system health check failed"
                });
            }
        }

        /// <summary>
        /// Batch invokes multiple tools in sequence.
        /// </summary>
        /// <param name="requests">Array of tool invocation requests</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Collection of tool invocation responses</returns>
        /// <response code="200">Batch execution completed (individual results may vary)</response>
        /// <response code="400">Invalid request format</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpPost("batch")]
        [ProducesResponseType(typeof(IEnumerable<ToolInvocationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<ToolInvocationResponse>>> BatchInvokeToolsAsync(
            [FromBody] BatchToolInvocationRequest requests,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Received batch tool invocation request for {ToolCount} tools", requests?.Tools?.Count ?? 0);

            try
            {
                if (requests?.Tools == null || requests.Tools.Count == 0)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "INVALID_BATCH_REQUEST",
                        Message = "Batch request must contain at least one tool invocation"
                    });
                }

                var responses = new List<ToolInvocationResponse>();

                // Execute tools sequentially (could be made parallel with careful consideration)
                foreach (var toolRequest in requests.Tools)
                {
                    try
                    {
                        var response = await _toolInvoker.InvokeToolAsync(
                            toolRequest.ToolId, 
                            toolRequest.Request, 
                            cancellationToken);
                        responses.Add(response);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in batch execution for tool {ToolId}", toolRequest.ToolId);
                        responses.Add(new ToolInvocationResponse
                        {
                            InvocationId = Guid.NewGuid().ToString(),
                            Status = ToolExecutionStatus.Failed,
                            ErrorMessage = ex.Message,
                            ExecutionTimeMs = 0
                        });
                    }
                }

                _logger.LogInformation("Batch tool execution completed. {SuccessCount}/{TotalCount} succeeded", 
                    responses.Count(r => r.Status == ToolExecutionStatus.Success), 
                    responses.Count);

                return Ok(responses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch tool invocation");
                return StatusCode(500, new ErrorResponse
                {
                    Code = "INTERNAL_ERROR",
                    Message = "An internal error occurred during batch tool invocation"
                });
            }
        }
    }

    /// <summary>
    /// Represents a batch tool invocation request.
    /// </summary>
    public class BatchToolInvocationRequest
    {
        /// <summary>
        /// List of tool invocations to execute.
        /// </summary>
        public List<SingleToolInvocation> Tools { get; set; } = new();

        /// <summary>
        /// Whether to stop execution on first failure.
        /// </summary>
        public bool StopOnError { get; set; } = false;
    }

    /// <summary>
    /// Represents a single tool invocation within a batch.
    /// </summary>
    public class SingleToolInvocation
    {
        /// <summary>
        /// Tool identifier.
        /// </summary>
        public string ToolId { get; set; } = null!;

        /// <summary>
        /// Tool invocation request.
        /// </summary>
        public ToolInvocationRequest Request { get; set; } = null!;
    }
} 