using System.ComponentModel.DataAnnotations;

namespace AgctorSDK.Host.Models
{
    /// <summary>
    /// Represents a message request to be sent to an agent via HTTP API.
    /// This DTO maps to the IMessageEnvelope interface for routing through the Actor Model.
    /// </summary>
    public class MessageRequest
    {
        /// <summary>
        /// The message payload/content to be sent to the agent.
        /// Can be any JSON-serializable object.
        /// </summary>
        public object? Payload { get; set; }

        /// <summary>
        /// Optional metadata dictionary for additional context.
        /// Follows Model Context Protocol (MCP) conventions.
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Optional headers for routing and protocol-level information.
        /// Used for specifying message type, content type, etc.
        /// </summary>
        public Dictionary<string, string>? Headers { get; set; }

        /// <summary>
        /// Optional sender ID for message attribution.
        /// If not provided, will be set to "http-api".
        /// </summary>
        public string? SenderId { get; set; }
    }

    /// <summary>
    /// Represents the response from sending a message to an agent.
    /// </summary>
    public class MessageResponse
    {
        /// <summary>
        /// Unique identifier for the message that was sent.
        /// </summary>
        public string MessageId { get; set; } = null!;

        /// <summary>
        /// Status of the message sending operation.
        /// </summary>
        public MessageStatus Status { get; set; }

        /// <summary>
        /// Optional response data if the message was a request-response pattern.
        /// </summary>
        public object? ResponseData { get; set; }

        /// <summary>
        /// Optional trace identifier (from OpenTelemetry) for this message.
        /// Used by the dashboard to render per-message traces.
        /// </summary>
        public string? TraceId { get; set; }

        /// <summary>
        /// Optional error message if the operation failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Timestamp when the message was processed.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Enumeration of possible message status values.
    /// </summary>
    public enum MessageStatus
    {
        /// <summary>Message was successfully sent and processed.</summary>
        Success,
        /// <summary>Message sending failed due to an error.</summary>
        Failed,
        /// <summary>Message was sent but processing is still in progress.</summary>
        Processing,
        /// <summary>Target agent was not found.</summary>
        AgentNotFound
    }

    /// <summary>
    /// Represents information about an agent in the system.
    /// Used for agent discovery endpoints.
    /// </summary>
    public class AgentInfo
    {
        /// <summary>
        /// Unique identifier of the agent.
        /// </summary>
        public string Id { get; set; } = null!;

        /// <summary>
        /// Type/class name of the agent.
        /// </summary>
        public string Type { get; set; } = null!;

        /// <summary>
        /// Current status of the agent (Active, Inactive, etc.).
        /// </summary>
        public AgentStatus Status { get; set; }

        /// <summary>
        /// Optional metadata about the agent.
        /// Can include capabilities, configuration, etc.
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Timestamp when the agent was created or last updated.
        /// </summary>
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Agent info plus type-specific detail for dashboard (PRD-006). GET /api/agents/{id}/detail.
    /// </summary>
    public class AgentDetailResponse
    {
        public string Id { get; set; } = null!;
        public string Type { get; set; } = null!;
        public AgentStatus Status { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Type-specific detail (e.g. LLM URL/model, CoderAgent tools, etc.).</summary>
        public object? Detail { get; set; }
    }

    /// <summary>
    /// Enumeration of possible agent status values.
    /// </summary>
    public enum AgentStatus
    {
        /// <summary>Agent is active and processing messages.</summary>
        Active,
        /// <summary>Agent is inactive or stopped.</summary>
        Inactive,
        /// <summary>Agent is in an error state.</summary>
        Error,
        /// <summary>Agent is initializing.</summary>
        Initializing
    }

    /// <summary>
    /// Represents a tool invocation request.
    /// Used for direct tool execution without agent wrapper.
    /// </summary>
    public class ToolInvocationRequest
    {
        /// <summary>
        /// Parameters required for tool execution.
        /// The structure depends on the specific tool being invoked.
        /// </summary>
        public Dictionary<string, object>? Parameters { get; set; }

        /// <summary>
        /// Optional execution context or metadata.
        /// </summary>
        public Dictionary<string, object>? Context { get; set; }

        /// <summary>
        /// Optional timeout for tool execution in seconds.
        /// If not specified, uses the tool's default timeout.
        /// </summary>
        public int? TimeoutSeconds { get; set; }
    }

    /// <summary>
    /// Represents the response from a tool invocation.
    /// </summary>
    public class ToolInvocationResponse
    {
        /// <summary>
        /// Unique identifier for the tool invocation.
        /// </summary>
        public string InvocationId { get; set; } = null!;

        /// <summary>
        /// Status of the tool execution.
        /// </summary>
        public ToolExecutionStatus Status { get; set; }

        /// <summary>
        /// The result/output from the tool execution.
        /// Can be any JSON-serializable object.
        /// </summary>
        public object? Result { get; set; }

        /// <summary>
        /// Optional error message if the execution failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Execution time in milliseconds.
        /// </summary>
        public long ExecutionTimeMs { get; set; }

        /// <summary>
        /// Timestamp when the tool execution completed.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Enumeration of possible tool execution status values.
    /// </summary>
    public enum ToolExecutionStatus
    {
        /// <summary>Tool executed successfully.</summary>
        Success,
        /// <summary>Tool execution failed.</summary>
        Failed,
        /// <summary>Tool execution timed out.</summary>
        Timeout,
        /// <summary>Tool was not found.</summary>
        ToolNotFound,
        /// <summary>Invalid parameters provided.</summary>
        InvalidParameters
    }

    /// <summary>
    /// Represents an error response from the API.
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// Error code or identifier.
        /// </summary>
        public string Code { get; set; } = null!;

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message { get; set; } = null!;

        /// <summary>
        /// Optional additional details about the error.
        /// </summary>
        public object? Details { get; set; }

        /// <summary>
        /// Timestamp when the error occurred.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }

    public record BatchToolRequest(
        List<BatchToolItem> Tools,
        bool ExecuteInParallel = false
    );

    public record BatchToolItem(
        string ToolId,
        Dictionary<string, object> Parameters
    );

    public record BatchToolResponse(
        bool Success,
        List<BatchToolResult> Results,
        TimeSpan TotalExecutionTime
    );

    public record BatchToolResult(
        string ToolId,
        bool Success,
        object? Result,
        string? ErrorMessage,
        TimeSpan ExecutionTime
    );

    // New models for agent creation
    public record AgentCreationRequest(
        string AgentId,
        string AgentType,
        Dictionary<string, object>? Configuration = null,
        string? ParentAgentId = null
    );

    public record AgentCreationResponse(
        bool Success,
        string? AgentId,
        string? AgentType,
        string? ErrorMessage
    );

    // New models for scenario setup
    public record ScenarioSetupRequest(
        string ScenarioName,
        Dictionary<string, object>? Parameters = null
    );

    public record ScenarioSetupResponse(
        bool Success,
        string ScenarioName,
        List<string> CreatedAgentIds,
        Dictionary<string, string> AgentRoles,
        string? ErrorMessage
    );

    /// <summary>
    /// Response for GET /api/test/current-scenario. The scenario last applied in this session.
    /// </summary>
    public record CurrentScenarioResponse(
        string ScenarioName,
        string? Description
    );

    /// <summary>
    /// Response model for trace visualization (per-message flow diagram).
    /// </summary>
    public class TraceVisualizationResponse
    {
        /// <summary>Trace identifier (e.g. OpenTelemetry trace id).</summary>
        public string TraceId { get; set; } = string.Empty;

        /// <summary>Mermaid sequenceDiagram text representing the message flow.</summary>
        public string Mermaid { get; set; } = string.Empty;

        /// <summary>Optional external viewer URL (Jaeger/Zipkin) for this trace.</summary>
        public string? ExternalViewerUrl { get; set; }
    }

    /// <summary>
    /// Timeline response used by the dashboard trace timeline component.
    /// </summary>
    public class TraceTimelineResponse
    {
        public string TraceId { get; set; } = string.Empty;
        public DateTimeOffset? StartedAtUtc { get; set; }
        public double TotalDurationMs { get; set; }
        public string? ExternalViewerUrl { get; set; }
        public List<TraceTimelineEventDto> Events { get; set; } = new();
    }

    /// <summary>
    /// Single event/span in a trace timeline.
    /// </summary>
    public class TraceTimelineEventDto
    {
        public string Id { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int Sequence { get; set; }
        public int Depth { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public double StartOffsetMs { get; set; }
        public double DurationMs { get; set; }
        public bool HasResult { get; set; }
    }
} 