using System.Diagnostics;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services
{
    /// <summary>
    /// Interface for directly invoking tools without agent wrapper.
    /// Provides a simplified interface for tool execution via HTTP API.
    /// </summary>
    public interface IToolInvoker
    {
        /// <summary>
        /// Invokes a tool by its identifier with the provided parameters.
        /// </summary>
        /// <param name="toolId">Unique identifier of the tool to invoke</param>
        /// <param name="request">Tool invocation request with parameters and context</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Tool invocation response with result and execution details</returns>
        Task<ToolInvocationResponse> InvokeToolAsync(string toolId, ToolInvocationRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a list of available tools that can be invoked.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Collection of available tool identifiers</returns>
        Task<IEnumerable<string>> GetAvailableToolsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets information about a specific tool including its parameters.
        /// </summary>
        /// <param name="toolId">Tool identifier</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Tool information or null if not found</returns>
        Task<ToolInfo?> GetToolInfoAsync(string toolId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents information about a tool.
    /// Used for tool discovery and parameter validation.
    /// </summary>
    public class ToolInfo
    {
        /// <summary>
        /// Unique identifier of the tool.
        /// </summary>
        public string Id { get; set; } = null!;

        /// <summary>
        /// Human-readable name of the tool.
        /// </summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// Description of what the tool does.
        /// </summary>
        public string Description { get; set; } = null!;

        /// <summary>
        /// Expected parameters for the tool.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>
        /// Tool version information.
        /// </summary>
        public string Version { get; set; } = "1.0.0";
    }

    /// <summary>
    /// Implementation of IToolInvoker that directly executes tools from the Core framework.
    /// Provides isolated tool execution following Actor Model principles.
    /// </summary>
    public class ToolInvoker : IToolInvoker
    {
        private readonly ILogger<ToolInvoker> _logger;
        private readonly IServiceProvider _serviceProvider;

        // Static registry of available tools (can be expanded to be dynamic)
        private static readonly Dictionary<string, ToolInfo> _availableTools = new()
        {
            ["file-system"] = new ToolInfo
            {
                Id = "file-system",
                Name = "File System Tool",
                Description = "Performs file system operations like read, write, list directories",
                Parameters = new Dictionary<string, object>
                {
                    ["operation"] = "string: read, write, list, delete",
                    ["path"] = "string: file or directory path",
                    ["content"] = "string: content for write operations (optional)"
                }
            },
            ["code-executor"] = new ToolInfo
            {
                Id = "code-executor",
                Name = "Code Executor Tool",
                Description = "Executes code in various languages (Python, C#, etc.)",
                Parameters = new Dictionary<string, object>
                {
                    ["language"] = "string: python, csharp, javascript",
                    ["code"] = "string: code to execute",
                    ["timeout"] = "int: execution timeout in seconds (optional)"
                }
            },
            ["code-editor"] = new ToolInfo
            {
                Id = "code-editor",
                Name = "Code Editor Tool",
                Description = "Edits and manipulates code files",
                Parameters = new Dictionary<string, object>
                {
                    ["operation"] = "string: edit, format, analyze",
                    ["file"] = "string: file path",
                    ["changes"] = "object: changes to apply (optional)"
                }
            }
        };

        public ToolInvoker(ILogger<ToolInvoker> logger, IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Invokes a tool directly with the provided parameters.
        /// Implements proper error handling and timeout management.
        /// </summary>
        public async Task<ToolInvocationResponse> InvokeToolAsync(string toolId, ToolInvocationRequest request, CancellationToken cancellationToken = default)
        {
            var invocationId = Guid.NewGuid().ToString();
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation("Invoking tool {ToolId} with invocation ID {InvocationId}", toolId, invocationId);

            try
            {
                // Validate tool exists
                if (!_availableTools.ContainsKey(toolId))
                {
                    _logger.LogWarning("Tool {ToolId} not found", toolId);
                    return new ToolInvocationResponse
                    {
                        InvocationId = invocationId,
                        Status = ToolExecutionStatus.ToolNotFound,
                        ErrorMessage = $"Tool '{toolId}' not found",
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // Validate parameters
                if (request.Parameters == null || request.Parameters.Count == 0)
                {
                    _logger.LogWarning("No parameters provided for tool {ToolId}", toolId);
                    return new ToolInvocationResponse
                    {
                        InvocationId = invocationId,
                        Status = ToolExecutionStatus.InvalidParameters,
                        ErrorMessage = "No parameters provided",
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // Setup timeout
                var timeout = request.TimeoutSeconds.HasValue
                    ? TimeSpan.FromSeconds(request.TimeoutSeconds.Value)
                    : TimeSpan.FromMinutes(5); // Default 5 minute timeout

                using var timeoutCts = new CancellationTokenSource(timeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                // Execute the tool
                var result = await ExecuteToolAsync(toolId, request.Parameters, request.Context, combinedCts.Token);

                stopwatch.Stop();
                _logger.LogInformation("Tool {ToolId} executed successfully in {ElapsedMs}ms", toolId, stopwatch.ElapsedMilliseconds);

                return new ToolInvocationResponse
                {
                    InvocationId = invocationId,
                    Status = ToolExecutionStatus.Success,
                    Result = result,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                _logger.LogWarning("Tool {ToolId} execution was cancelled", toolId);
                return new ToolInvocationResponse
                {
                    InvocationId = invocationId,
                    Status = ToolExecutionStatus.Failed,
                    ErrorMessage = "Operation was cancelled",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.LogWarning("Tool {ToolId} execution timed out", toolId);
                return new ToolInvocationResponse
                {
                    InvocationId = invocationId,
                    Status = ToolExecutionStatus.Timeout,
                    ErrorMessage = "Tool execution timed out",
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Tool {ToolId} execution failed", toolId);
                return new ToolInvocationResponse
                {
                    InvocationId = invocationId,
                    Status = ToolExecutionStatus.Failed,
                    ErrorMessage = ex.Message,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        /// <summary>
        /// Gets the list of available tools.
        /// </summary>
        public async Task<IEnumerable<string>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask; // Placeholder for async consistency
            return _availableTools.Keys.ToList();
        }

        /// <summary>
        /// Gets information about a specific tool.
        /// </summary>
        public async Task<ToolInfo?> GetToolInfoAsync(string toolId, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask; // Placeholder for async consistency
            return _availableTools.TryGetValue(toolId, out var toolInfo) ? toolInfo : null;
        }

        /// <summary>
        /// Executes the specified tool with the given parameters.
        /// This is a simplified implementation that can be extended with actual tool integration.
        /// </summary>
        private async Task<object> ExecuteToolAsync(string toolId, Dictionary<string, object> parameters, Dictionary<string, object>? context, CancellationToken cancellationToken)
        {
            // This is a simplified implementation for demonstration
            // In a real implementation, this would integrate with the actual tool actors from AgctorSDK.Core
            
            _logger.LogDebug("Executing tool {ToolId} with parameters: {@Parameters}", toolId, parameters);

            return toolId switch
            {
                "file-system" => await ExecuteFileSystemTool(parameters, cancellationToken),
                "code-executor" => await ExecuteCodeExecutorTool(parameters, cancellationToken),
                "code-editor" => await ExecuteCodeEditorTool(parameters, cancellationToken),
                _ => throw new NotSupportedException($"Tool '{toolId}' is not supported")
            };
        }

        /// <summary>
        /// Simulated file system tool execution.
        /// In production, this would integrate with the actual FileSystemTool from Core.
        /// </summary>
        private async Task<object> ExecuteFileSystemTool(Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            await Task.Delay(100, cancellationToken); // Simulate work
            
            var operation = parameters.TryGetValue("operation", out var op) ? op.ToString() : "list";
            var path = parameters.TryGetValue("path", out var p) ? p.ToString() : "/";

            return new
            {
                operation,
                path,
                result = $"Simulated {operation} operation on {path}",
                timestamp = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// Simulated code executor tool execution.
        /// In production, this would integrate with the actual CodeExecutorTool from Core.
        /// </summary>
        private async Task<object> ExecuteCodeExecutorTool(Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            await Task.Delay(200, cancellationToken); // Simulate work
            
            var language = parameters.TryGetValue("language", out var lang) ? lang.ToString() : "python";
            var code = parameters.TryGetValue("code", out var c) ? c.ToString() : "";

            return new
            {
                language,
                code,
                output = $"Simulated execution of {language} code",
                exitCode = 0,
                timestamp = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// Simulated code editor tool execution.
        /// In production, this would integrate with the actual CodeEditorTool from Core.
        /// </summary>
        private async Task<object> ExecuteCodeEditorTool(Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            await Task.Delay(150, cancellationToken); // Simulate work
            
            var operation = parameters.TryGetValue("operation", out var op) ? op.ToString() : "edit";
            var file = parameters.TryGetValue("file", out var f) ? f.ToString() : "";

            return new
            {
                operation,
                file,
                result = $"Simulated {operation} operation on {file}",
                timestamp = DateTimeOffset.UtcNow
            };
        }
    }
} 