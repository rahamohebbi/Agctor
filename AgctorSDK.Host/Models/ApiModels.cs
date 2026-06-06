using System.ComponentModel.DataAnnotations;
using AgctorSDK.Host.Services.Scenarios;

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

        /// <summary>
        /// Optional chat session identifier used for session memory.
        /// </summary>
        public string? SessionId { get; set; }
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
    /// Unified agent definition record for dashboard catalog view.
    /// </summary>
    public class AgentDefinitionDto
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty; // csharp-type | project-memory-yaml
        public string Source { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty; // enabled | disabled | valid
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>GET /api/agents/definitions/{id} — one of C# type options or a project-memory YAML spec.</summary>
    public class AgentDefinitionDetailDto
    {
        public string Kind { get; set; } = ""; // csharp-type | project-memory-yaml
        public string Id { get; set; } = "";
        /// <summary>For <c>csharp-type</c>: <see cref="CSharpAgentDefinitionDetailDto"/>. For YAML: <see cref="AgentDetailDto"/>.</summary>
        public object? Detail { get; set; }
    }

    /// <summary>Payload inside <see cref="AgentDefinitionDetailDto.Detail"/> when <c>Kind</c> is <c>csharp-type</c>.</summary>
    public class CSharpAgentDefinitionDetailDto
    {
        public bool Enabled { get; set; }
        public string ClrType { get; set; } = "";
    }

    /// <summary>PUT /api/agents/types/{typeName}/enabled — dashboard agent-type toggle (PRD-010).</summary>
    public class AgentTypeEnableRequest
    {
        public bool Enabled { get; set; }
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
        string? ScenarioName = null,
        Dictionary<string, object>? Parameters = null
    );

    /// <summary>POST /api/scenarios/{id}/apply — optional parameters for scripted handlers.</summary>
    public record ScenarioApplyRequest(Dictionary<string, object>? Parameters = null);

    /// <summary>POST /api/scenarios/{id}/flow/run — executes PRD-014 flow (sequential + parallel→Merge; LlmNode uses project memory).</summary>
    public sealed class ScenarioFlowRunRequest
    {
        public string Message { get; set; } = "";

        /// <summary>Optional session id for transcript-aware persona prompts (same store as playground).</summary>
        public string? SessionId { get; set; }

        /// <summary>Per <c>LlmNode</c> wall-clock timeout (seconds). Default 180 when omitted; use 0 to disable (only HTTP cancellation).</summary>
        public int? LlmNodeTimeoutSeconds { get; set; }

        /// <summary>PRD-024: visual asset ids for delta ingest on resume.</summary>
        public List<string>? AttachmentIds { get; set; }

        /// <summary>Set by execution service from project memory options.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? ProjectRoot { get; set; }
    }

    /// <summary>Result of <see cref="ScenarioFlowRunRequest"/>.</summary>
    public sealed class ScenarioFlowRunResponse
    {
        public bool Success { get; set; }
        public bool Completed { get; set; } = true;
        public string? Output { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>PRD-024: Running | WaitingForUserInput | WaitingForDomainEvent | Completed | Failed</summary>
        public string? Status { get; set; }

        /// <summary>PRD-024: node id where execution is active or suspended.</summary>
        public string? ExecutionNodeId { get; set; }

        /// <summary>PRD-024: prompt shown when <see cref="Completed"/> is false and status is WaitingForUserInput.</summary>
        public string? PendingPrompt { get; set; }

        public static ScenarioFlowRunResponse OkCompleted(string output) =>
            OkCompleted(output, null, "Completed");

        public static ScenarioFlowRunResponse OkCompleted(string output, string? executionNodeId, string? status) =>
            new()
            {
                Success = true,
                Completed = true,
                Output = output,
                Status = status ?? "Completed",
                ExecutionNodeId = executionNodeId
            };

        public static ScenarioFlowRunResponse OkSuspended(string pendingPrompt, string? executionNodeId, string? status) =>
            new()
            {
                Success = true,
                Completed = false,
                PendingPrompt = pendingPrompt,
                Output = pendingPrompt,
                Status = status,
                ExecutionNodeId = executionNodeId
            };

        public static ScenarioFlowRunResponse Fail(string code, string message) =>
            new() { Success = false, Completed = false, ErrorCode = code, ErrorMessage = message };
    }

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

    /// <summary>Scenario catalog record (JSON-backed).</summary>
    public sealed class ScenarioDto
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string? Handler { get; set; }
        public List<string> AgentTypes { get; set; } = new();
        /// <summary>Project-memory YAML persona ids attached to this scenario (non-runtime).</summary>
        public List<string> PersonaAgentIds { get; set; } = new();
        /// <summary>Optional role bindings into <see cref="PersonaAgentIds"/>.</summary>
        public ScenarioPersonaBindingsDto PersonaBindings { get; set; } = new();

        /// <summary>PRD-014 optional visual flow (canonical GraphDocument).</summary>
        public ScenarioFlowDocument? Flow { get; set; }
    }

    public sealed class ScenarioPersonaBindingsDto
    {
        public string? Extractor { get; set; }
        public string? Curator { get; set; }
        public string? Query { get; set; }
    }

    public sealed class ScenarioCatalogUpdateRequest
    {
        public int Version { get; set; } = 1;
        public List<ScenarioDto> Scenarios { get; set; } = new();
        /// <summary>When set, hides matching ids from the default catalog in the merged view (dashboard saves this with the scenario list).</summary>
        public List<string>? SuppressedDefaultScenarioIds { get; set; }
    }

    /// <summary>Creates a declarative scenario in the user catalog file.</summary>
    public sealed class CreateScenarioRequest
    {
        public string Id { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>
    /// Request payload for creating a new chat session.
    /// </summary>
    public class CreateChatSessionRequest
    {
        public string? Title { get; set; }
        public string? SessionId { get; set; }

        /// <summary>Optional chat project id — session is created inside this bucket when set.</summary>
        public string? ProjectId { get; set; }
    }

    /// <summary>Creates a chat project linked to a scenario id.</summary>
    public class CreateChatProjectRequest
    {
        public string? ProjectId { get; set; }
        public string? Name { get; set; }
        public string? ScenarioId { get; set; }
        /// <summary>Entity slug for coref focus (e.g. <c>ryan</c>).</summary>
        public string? FocusEntityKey { get; set; }
        public string? FocusDisplayName { get; set; }
        /// <summary>Recent photos included in visual context (1–12). Stored in project settings_json.</summary>
        public int? VisualMaxPhotos { get; set; }
    }

    /// <summary>Updates chat project metadata.</summary>
    public class UpdateChatProjectRequest
    {
        public string? Name { get; set; }
        public string? ScenarioId { get; set; }
        public string? FocusEntityKey { get; set; }
        public string? FocusDisplayName { get; set; }
        public int? VisualMaxPhotos { get; set; }
    }

    /// <summary>Updates a chat session (currently: title only).</summary>
    public class UpdateChatSessionRequest
    {
        public string? Title { get; set; }
    }

    /// <summary>Assigns a session to a project (<c>PUT /api/chat/sessions/.../project</c>).</summary>
    public class AssignChatSessionProjectRequest
    {
        public string ProjectId { get; set; } = "";
    }

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

        /// <summary>Optional JSON for dashboard drill-down (e.g. playground LLM I/O, written paths).</summary>
        public string? TimelineDetailJson { get; set; }

        /// <summary>UI category: tool, llm, ingest, persist, resolve, http, other.</summary>
        public string? EventKind { get; set; }

        /// <summary>ok | error | running — for badges and row styling.</summary>
        public string? Status { get; set; }
    }
} 