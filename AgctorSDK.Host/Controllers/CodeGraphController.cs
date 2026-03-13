using AgctorSDK.Host.Models;
using AgctorSDK.Host.Services;
using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace AgctorSDK.Host.Controllers;

/// <summary>
/// Exposes current CodeGraph context for the dashboard when code-graph-demo scenario is active (PRD-006).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CodeGraphController : ControllerBase
{
    private readonly ICodeGraphContextAccessor _contextAccessor;
    private readonly ILogger<CodeGraphController> _logger;

    public CodeGraphController(
        ICodeGraphContextAccessor contextAccessor,
        ILogger<CodeGraphController> logger)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the current CodeGraph context (actor tree + embedding summary) when code-graph-demo has been set up; otherwise 404.
    /// Uses live embedding count when available (after "Index now" on the dashboard).
    /// </summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(CodeGraphContextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CodeGraphContextDto>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var context = await _contextAccessor.GetCurrentAsync(cancellationToken);
        if (context == null)
        {
            _logger.LogDebug("CodeGraph context requested but no active context");
            return NotFound();
        }
        return Ok(context);
    }

    /// <summary>
    /// Returns all embedding records (actor ID, text, vector) for debugging and visualization when code-graph-demo is active.
    /// </summary>
    [HttpGet("embeddings")]
    [ProducesResponseType(typeof(IReadOnlyList<EmbeddingRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EmbeddingRecordDto>>> GetEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        if (_contextAccessor.GetCurrent() == null)
        {
            _logger.LogDebug("Embeddings requested but no CodeGraph context active");
            return NotFound();
        }
        var records = await _contextAccessor.GetEmbeddingRecordsAsync(cancellationToken);
        return Ok(records);
    }

    /// <summary>
    /// Returns the content of a file that belongs to the active CodeGraph actor tree.
    /// </summary>
    [HttpGet("file-content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> GetFileContentAsync([FromQuery] string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new { message = "A file path is required." });
        }

        var context = await _contextAccessor.GetCurrentAsync(cancellationToken);
        if (context?.ActorTree == null)
        {
            _logger.LogDebug("File content requested but no CodeGraph context is active");
            return NotFound(new { message = "CodeGraph context is not active." });
        }

        var fileNode = FindFileNode(context.ActorTree, path);
        if (fileNode?.PhysicalPath == null || !System.IO.File.Exists(fileNode.PhysicalPath))
        {
            _logger.LogDebug("File content requested for path {Path} but it is not in the active actor tree", path);
            return NotFound(new { message = "The selected file is not available in the active actor tree." });
        }

        var content = await System.IO.File.ReadAllTextAsync(fileNode.PhysicalPath, cancellationToken);
        return Ok(new
        {
            fileName = fileNode.Name,
            path = fileNode.PhysicalPath,
            content
        });
    }

    private static AgctorSDK.CodeGraph.Persistence.ActorSerializer.ActorDto? FindFileNode(
        AgctorSDK.CodeGraph.Persistence.ActorSerializer.ActorDto node,
        string path)
    {
        if (string.Equals(node.PhysicalPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        if (node.Children == null)
        {
            return null;
        }

        foreach (var child in node.Children)
        {
            var match = FindFileNode(child, path);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }
}
