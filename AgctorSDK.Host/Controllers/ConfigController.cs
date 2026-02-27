using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// Exposes Host configuration for the dashboard (PRD-006).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ConfigController : ControllerBase
{
    private readonly IHostConfigurationService _configService;
    private readonly ILogger<ConfigController> _logger;

    public ConfigController(
        IHostConfigurationService configService,
        ILogger<ConfigController> logger)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns full Host configuration: runtime, LLM, MCP, paths, background services, agent types, tools, scenarios.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(HostConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<HostConfigurationDto>> GetConfiguration(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Dashboard config requested");
            var config = await _configService.GetConfigurationAsync(cancellationToken);
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Host configuration");
            return StatusCode(500, new ErrorResponse
            {
                Code = "CONFIG_ERROR",
                Message = "An error occurred while retrieving configuration"
            });
        }
    }
}
