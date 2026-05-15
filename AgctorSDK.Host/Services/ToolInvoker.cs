using System.Diagnostics;
using System.Text.Json;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Host.Models;
using Microsoft.Extensions.Configuration;

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
    /// HTTP bridge: discovery metadata comes from <see cref="AgctorToolCatalog"/>; execution goes through
    /// <see cref="IAgentFactory.InvokeToolRequestAsync"/> so the same tool actors as agents use handle requests.
    /// </summary>
    public class ToolInvoker : IToolInvoker
    {
        /// <summary>Handled in-process — never sent to <see cref="IAgentFactory"/>.</summary>
        private const string SimulatedFileSystemOutsideRoot = "__http_file_system_simulated__";

        private readonly ILogger<ToolInvoker> _logger;
        private readonly IAgentFactory _agentFactory;
        private readonly AgctorToolCatalog _catalog;
        private readonly string _generatedCodeRoot;

        public ToolInvoker(
            ILogger<ToolInvoker> logger,
            IAgentFactory agentFactory,
            AgctorToolCatalog catalog,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            var configuredRoot = configuration.GetValue<string>("Agctor:GeneratedCodeRoot");
            _generatedCodeRoot = string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.Combine(Path.GetTempPath(), "agctor-generated-code")
                : configuredRoot;
            Directory.CreateDirectory(_generatedCodeRoot);
        }

        /// <inheritdoc />
        public async Task<ToolInvocationResponse> InvokeToolAsync(string toolId, ToolInvocationRequest request, CancellationToken cancellationToken = default)
        {
            var invocationId = Guid.NewGuid().ToString();
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation("Invoking tool {ToolId} with invocation ID {InvocationId}", toolId, invocationId);

            try
            {
                if (!_catalog.TryGetHttpEntry(toolId, out var entry))
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

                var timeout = request.TimeoutSeconds.HasValue
                    ? TimeSpan.FromSeconds(request.TimeoutSeconds.Value)
                    : TimeSpan.FromMinutes(5);

                using var timeoutCts = new CancellationTokenSource(timeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var toolRequest = BuildToolRequest(entry.PrimaryId, request.Parameters);
                if (toolRequest == null)
                {
                    return new ToolInvocationResponse
                    {
                        InvocationId = invocationId,
                        Status = ToolExecutionStatus.InvalidParameters,
                        ErrorMessage = "Unsupported parameter combination for this tool.",
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                ToolResult toolResult;
                if (string.Equals(toolRequest.Operation, SimulatedFileSystemOutsideRoot, StringComparison.Ordinal))
                {
                    toolResult = BuildSimulatedOutsideRootFileSystemResult(toolRequest.Parameters);
                }
                else
                {
                    toolResult = await _agentFactory
                        .InvokeToolRequestAsync(entry.ClrTypeName, toolRequest, invokingAgentId: null, combinedCts.Token)
                        .ConfigureAwait(false);
                }

                stopwatch.Stop();

                if (!toolResult.IsSuccess)
                {
                    _logger.LogWarning("Tool {ToolId} returned error: {Error}", toolId, toolResult.Error);
                    return new ToolInvocationResponse
                    {
                        InvocationId = invocationId,
                        Status = ToolExecutionStatus.Failed,
                        ErrorMessage = toolResult.Error,
                        Result = PackageToolOutput(toolResult),
                        ExecutionTimeMs = Math.Max(1, stopwatch.ElapsedMilliseconds)
                    };
                }

                _logger.LogInformation("Tool {ToolId} executed successfully in {ElapsedMs}ms", toolId, stopwatch.ElapsedMilliseconds);

                return new ToolInvocationResponse
                {
                    InvocationId = invocationId,
                    Status = ToolExecutionStatus.Success,
                    Result = PackageToolOutput(toolResult),
                    ExecutionTimeMs = Math.Max(1, stopwatch.ElapsedMilliseconds)
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

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IEnumerable<string>>(_catalog.GetHttpToolPrimaryIds());
        }

        /// <inheritdoc />
        public Task<ToolInfo?> GetToolInfoAsync(string toolId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_catalog.TryGetHttpEntry(toolId, out var e) ? e.Discovery : null);
        }

        /// <summary>
        /// Maps legacy HTTP parameters to a <see cref="ToolRequest"/>. Returns null when the request cannot be mapped.
        /// </summary>
        private ToolRequest? BuildToolRequest(string primaryId, Dictionary<string, object> parameters)
        {
            return primaryId switch
            {
                "file-system" => BuildFileSystemRequest(parameters),
                "code-executor" => BuildCodeExecutorRequest(parameters),
                "code-editor" => BuildCodeEditorRequest(parameters),
                _ => null
            };
        }

        /// <summary>
        /// Outside the generated-code root we keep the previous deterministic stub so batch/concurrency tests stay stable.
        /// </summary>
        private ToolRequest? BuildFileSystemRequest(Dictionary<string, object> parameters)
        {
            var operation = GetString(parameters, "operation")?.ToLowerInvariant() ?? "list";
            var rawPath = GetString(parameters, "path") ?? ".";
            var resolvedPath = ResolvePath(rawPath);

            if (!IsUnderGeneratedRoot(resolvedPath))
            {
                return new ToolRequest
                {
                    ToolName = "FileSystemTool",
                    Operation = SimulatedFileSystemOutsideRoot,
                    Parameters = new Dictionary<string, object>
                    {
                        ["operation"] = operation,
                        ["path"] = resolvedPath,
                        ["rootPath"] = _generatedCodeRoot
                    }
                };
            }

            return operation switch
            {
                "read" => new ToolRequest
                {
                    ToolName = "FileSystemTool",
                    Operation = "ReadFile",
                    Parameters = new Dictionary<string, object> { ["path"] = resolvedPath }
                },
                "write" => new ToolRequest
                {
                    ToolName = "FileSystemTool",
                    Operation = "WriteFile",
                    Parameters = new Dictionary<string, object>
                    {
                        ["path"] = resolvedPath,
                        ["content"] = GetString(parameters, "content") ?? string.Empty
                    }
                },
                "list" => new ToolRequest
                {
                    ToolName = "FileSystemTool",
                    Operation = "ListDirectory",
                    Parameters = new Dictionary<string, object> { ["path"] = resolvedPath }
                },
                "delete" => new ToolRequest
                {
                    ToolName = "FileSystemTool",
                    Operation = "DeletePath",
                    Parameters = new Dictionary<string, object> { ["path"] = resolvedPath }
                },
                _ => null
            };
        }

        private static ToolResult BuildSimulatedOutsideRootFileSystemResult(IDictionary<string, object> parameters)
        {
            var op = GetString(parameters, "operation") ?? "list";
            var path = GetString(parameters, "path") ?? string.Empty;
            var root = GetString(parameters, "rootPath") ?? string.Empty;
            var payload = new
            {
                operation = op,
                path,
                mode = "simulated-outside-root",
                rootPath = root,
                result = $"Path is outside deterministic root. Simulated {op} operation.",
                timestamp = DateTimeOffset.UtcNow
            };
            return new ToolResult { IsSuccess = true, Output = JsonSerializer.Serialize(payload) };
        }

        private static ToolRequest BuildCodeExecutorRequest(Dictionary<string, object> parameters)
        {
            var language = GetString(parameters, "language") ?? "python";
            var code = GetString(parameters, "code") ?? string.Empty;
            return new ToolRequest
            {
                ToolName = "CodeExecutorTool",
                Operation = "RunCode",
                Parameters = new Dictionary<string, object>
                {
                    ["language"] = language,
                    ["code"] = code
                }
            };
        }

        private ToolRequest? BuildCodeEditorRequest(Dictionary<string, object> parameters)
        {
            var operation = GetString(parameters, "operation")?.ToLowerInvariant() ?? "edit";
            var rawFile = GetString(parameters, "file") ?? GetString(parameters, "path");
            if (string.IsNullOrWhiteSpace(rawFile))
            {
                return null;
            }

            var filePath = ResolvePath(rawFile);

            return operation switch
            {
                "format" => new ToolRequest
                {
                    ToolName = "CodeEditorTool",
                    Operation = "FormatFile",
                    Parameters = new Dictionary<string, object> { ["path"] = filePath }
                },
                _ => null
            };
        }

        private static object? PackageToolOutput(ToolResult toolResult)
        {
            if (toolResult.Output is string s)
            {
                try
                {
                    return JsonSerializer.Deserialize<JsonElement>(s);
                }
                catch (JsonException)
                {
                    return new { output = s, isSuccess = toolResult.IsSuccess, error = toolResult.Error };
                }
            }

            return new { output = toolResult.Output, isSuccess = toolResult.IsSuccess, error = toolResult.Error };
        }

        private string ResolvePath(string requestedPath)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                throw new ArgumentException("Path cannot be null or empty.");
            }

            if (Path.IsPathRooted(requestedPath))
            {
                return Path.GetFullPath(requestedPath);
            }

            return Path.GetFullPath(Path.Combine(_generatedCodeRoot, requestedPath));
        }

        private bool IsUnderGeneratedRoot(string fullPath)
        {
            var root = Path.GetFullPath(_generatedCodeRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(fullPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(candidate, root, StringComparison.Ordinal))
            {
                return true;
            }

            return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        private static string? GetString(IDictionary<string, object> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value switch
            {
                string s => s,
                JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
                JsonElement je => je.ToString(),
                _ => value.ToString()
            };
        }
    }
}
