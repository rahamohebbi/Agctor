using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using System.Text.RegularExpressions;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Basic implementation of an intelligent agent that can process prompts and spawn child agents.
    /// Provides recursive task decomposition capabilities and manages a hierarchy of child agents.
    /// </summary>
    public class Agent : IAgent
    {
        private readonly List<string> _childAgentIds = new();
        private readonly object _lockObject = new();
        private IAgentFactory? _agentFactory;
        private string? _currentPrompt;
        private string? _parentAgentId;
        private AgentStatus _status = AgentStatus.Idle;
        private ActorState _actorState = ActorState.Initializing;
        private int _hierarchyDepth = 0; // Track depth to prevent infinite recursion
        private const int MAX_HIERARCHY_DEPTH = 3; // Maximum allowed depth
        private const int MAX_CHILD_AGENTS = 5; // Maximum children per agent

        private Queue<string> _subtaskQueue = new Queue<string>();
        private object? _lastSubtaskResult;
        private readonly Dictionary<string, object> _subtaskResults = new Dictionary<string, object>();

        /// <summary>
        /// Unique identifier for this agent instance.
        /// </summary>
        public string Id { get; private set; } = string.Empty;

        /// <summary>
        /// The type/class name of this agent.
        /// </summary>
        public string ActorType => GetType().Name;

        /// <summary>
        /// Current state of the actor lifecycle.
        /// </summary>
        public ActorState State => _actorState;

        /// <summary>
        /// The current prompt or task that this agent is working on.
        /// </summary>
        public string? CurrentPrompt => _currentPrompt;

        /// <summary>
        /// The parent agent ID if this agent was spawned as a child agent.
        /// </summary>
        public string? ParentAgentId => _parentAgentId;

        /// <summary>
        /// Collection of child agent IDs that this agent has spawned for subtasks.
        /// </summary>
        public IReadOnlyList<string> ChildAgentIds
        {
            get
            {
                lock (_lockObject)
                {
                    return _childAgentIds.ToList();
                }
            }
        }

        /// <summary>
        /// The current status of the agent's work on its assigned prompt.
        /// </summary>
        public AgentStatus Status => _status;

        /// <summary>
        /// Gets the current hierarchy depth of this agent.
        /// </summary>
        public int HierarchyDepth => _hierarchyDepth;

        /// <summary>
        /// Event raised when the actor's state changes.
        /// </summary>
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Event raised when the agent's status changes.
        /// </summary>
        public event EventHandler<AgentStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// Event raised when the agent spawns a new child agent for a subtask.
        /// </summary>
        public event EventHandler<ChildAgentSpawnedEventArgs>? ChildAgentSpawned;

        /// <summary>
        /// Event raised when a child agent completes its assigned subtask.
        /// </summary>
        public event EventHandler<SubtaskCompletedEventArgs>? SubtaskCompleted;

        /// <summary>
        /// Gets the agent factory instance associated with this agent.
        /// This is set by the runtime during agent initialization and used by the agent
        /// to spawn child agents or interact with the runtime (e.g., HumanAgentAdapter).
        /// </summary>
        protected IAgentFactory? AgentFactory => _agentFactory;

        /// <summary>
        /// Initializes a new instance of the Agent class.
        /// </summary>
        /// <param name="id">The unique identifier for this agent</param>
        public Agent(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        /// <summary>
        /// Parameterless constructor for reflection-based instantiation.
        /// The ID will be set via reflection or during initialization.
        /// </summary>
        public Agent()
        {
            // ID will be set during initialization
        }

        /// <summary>
        /// Initializes the agent when it's first created or activated.
        /// </summary>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous initialization operation</returns>
        public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            LogInfo("Initializing agent...");
            
            ChangeActorState(ActorState.Active, "Agent initialized successfully");
            ChangeAgentStatus(AgentStatus.Idle, "Agent ready to receive prompts");
            
            LogInfo($"Agent initialization completed (Depth: {_hierarchyDepth})");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Gracefully shuts down the agent and cleans up resources.
        /// </summary>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous shutdown operation</returns>
        public virtual async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            LogInfo("Shutting down agent...");
            
            ChangeActorState(ActorState.Stopping, "Agent shutdown initiated");
            
            // Stop all child agents
            var childIds = ChildAgentIds.ToList();
            foreach (var childId in childIds)
            {
                try
                {
                    if (_agentFactory != null)
                    {
                        await _agentFactory.StopAgentAsync(childId, cancellationToken);
                        LogInfo($"Stopped child agent: {childId}");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error stopping child agent {childId}: {ex.Message}");
                }
            }
            
            ChangeActorState(ActorState.Stopped, "Agent shutdown completed");
            LogInfo("Agent shutdown completed");
        }

        /// <summary>
        /// Processes an incoming message envelope.
        /// </summary>
        /// <param name="envelope">The message envelope containing the message and metadata</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous message processing operation</returns>
        public virtual async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope == null)
            {
                LogWarning("Received null message envelope.");
                // Construct a valid error response according to MCP
                var errorPayload = "Error: Null envelope received.";
                var errorId = Guid.NewGuid().ToString();
                var errorMetadata = new Dictionary<string, object> { { "Timestamp", DateTimeOffset.UtcNow } };
                var errorHeaders = new Dictionary<string, string> 
                { 
                    { "SenderId", Id }, // This agent is sending the error
                    // ReceiverId might be unknown if envelope is null
                    { "MessageType", "ErrorResponse" } 
                };
                return new MessageEnvelope(payload: errorPayload, metadata: errorMetadata, id: errorId, headers: errorHeaders);
            }
            
            if (envelope.Payload == null)
            {   
                LogWarning("Received message envelope with null payload.");
                string originalSenderId = (envelope.Headers?.TryGetValue("SenderId", out var sid) == true ? sid : null) ?? "unknown";
                var errorPayload = "Error: Null payload in received envelope.";
                var errorId = envelope.Id ?? Guid.NewGuid().ToString(); // Try to use original Id
                var errorMetadata = new Dictionary<string, object> { { "Timestamp", DateTimeOffset.UtcNow } };
                if (envelope.Metadata?.TryGetValue("CorrelationId", out var corrId) == true) { errorMetadata["CorrelationId"] = corrId; }

                var errorHeaders = new Dictionary<string, string>
                {
                    { "SenderId", Id },
                    { "ReceiverId", originalSenderId },
                    { "MessageType", "ErrorResponse" }
                };
                return new MessageEnvelope(payload: errorPayload, metadata: errorMetadata, id: errorId, headers: errorHeaders);
            }

            LogInfo($"Received message with payload type: {envelope.Payload.GetType().Name}. MessageId: {envelope.Id}");

            try
            {
                // Handle different message types
                switch (envelope.Payload)
                {
                    case ProcessPromptMessage promptMsg:
                        await ProcessPromptAsync(promptMsg.Prompt, cancellationToken);
                        break;

                    case SubtaskCompletedMessage completedMsg:
                        LogInfo($"Received SubtaskCompletedMessage from child agent {completedMsg.ChildAgentId} with result type {completedMsg.Result?.GetType().Name ?? "null"}");
                        try
                        {
                            await HandleSubtaskCompletionAsync(completedMsg.ChildAgentId, completedMsg.Result, cancellationToken);
                            LogInfo($"Successfully handled subtask completion from child agent {completedMsg.ChildAgentId}");
                        }
                        catch (Exception ex)
                        {
                            LogError($"Error handling subtask completion: {ex.Message}");
                            LogError($"Stack Trace: {ex.StackTrace}");
                        }
                        break;

                    case SubtaskFailedMessage failedMsg:
                        LogInfo($"Received SubtaskFailedMessage from child agent {failedMsg.ChildAgentId} with error: {failedMsg.Error.Message}");
                        await HandleSubtaskFailureAsync(failedMsg.ChildAgentId, failedMsg.Error, cancellationToken);
                        break;

                    case GetAgentStatusMessage statusMsg:
                        await HandleStatusRequestAsync(statusMsg, envelope, cancellationToken);
                        break;

                    case StopAgentMessage stopMsg:
                        LogInfo($"Received stop request: {stopMsg.Reason}");
                        await ShutdownAsync(cancellationToken);
                        break;

                    default:
                        LogWarning($"Unknown message type: {envelope.Payload.GetType().Name}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing message {envelope.Payload.GetType().Name} (Id: {envelope.Id}): {ex.Message}");
                string originalSenderId = (envelope.Headers?.TryGetValue("SenderId", out var sid) == true ? sid : null) ?? "unknown";
                var errorPayload = $"Error processing message: {ex.Message}";
                var errorId = envelope.Id; // Use original Id for context

                var errorMetadata = new Dictionary<string, object> { { "Timestamp", DateTimeOffset.UtcNow } };
                if (envelope.Metadata?.TryGetValue("CorrelationId", out var corrId) == true) { errorMetadata["CorrelationId"] = corrId; }
                
                var errorHeaders = new Dictionary<string, string>
                {
                    { "SenderId", Id }, // This agent is the sender of the error message
                    { "ReceiverId", originalSenderId }, // Send error back to original sender
                    { "MessageType", "ErrorResponse" },
                    { "OriginalMessageId", envelope.Id } // Custom header for tracing
                };
                return new MessageEnvelope(payload: errorPayload, metadata: errorMetadata, id: Guid.NewGuid().ToString(), headers: errorHeaders);
            }
            // Return the original envelope as a default acknowledgment if no specific response was generated and no error occurred.
            // This behavior might need review based on specific message contracts.
            // For now, if a handler like ProcessPromptAsync is supposed to send its own response, 
            // it should return null or a specific response envelope.
            // If it returns void/Task, then this default return is an ACK.
            // Let's assume for now that if a message is processed without error and doesn't generate a specific reply
            // within this ReceiveAsync, we return an acknowledgement based on the original envelope.
            
            var ackMetadata = new Dictionary<string, object>(envelope.Metadata ?? new Dictionary<string, object>());
            ackMetadata["ProcessedTimestamp"] = DateTimeOffset.UtcNow;
            if (envelope.Metadata?.TryGetValue("CorrelationId", out var originalCorrId) == true)
            {
                ackMetadata["CorrelationId"] = originalCorrId; // Echo correlation ID
            }

            var ackHeaders = new Dictionary<string, string>(envelope.Headers ?? new Dictionary<string, string>());
            // Swap sender/receiver for the ACK, or set new ones
            string originalMsgSender = (envelope.Headers?.TryGetValue("SenderId", out var sId) == true ? sId : null) ?? "unknown";
            ackHeaders["SenderId"] = Id; // This agent is sending the ACK
            ackHeaders["ReceiverId"] = originalMsgSender; // Send ACK back to original sender
            ackHeaders["MessageType"] = (envelope.Headers?.TryGetValue("MessageType", out var mt) == true ? mt : "UnknownType") + "_Ack";

            return new MessageEnvelope(
                payload: $"Successfully processed message of type {envelope.Payload.GetType().Name}", 
                metadata: ackMetadata, 
                id: Guid.NewGuid().ToString(), // New ID for the ACK envelope
                headers: ackHeaders
            );
        }

        /// <summary>
        /// Processes a new prompt and begins working on the assigned task.
        /// </summary>
        /// <param name="prompt">The prompt or task description to process</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous prompt processing operation</returns>
        public virtual async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));

            LogInfo($"Processing prompt: {prompt}");
            
            _currentPrompt = prompt;
            ChangeAgentStatus(AgentStatus.Idle, $"Processing prompt: {prompt}");

            try
            {
                // This is where the agent would implement its core logic
                // For this basic implementation, we'll simulate some work and potentially spawn child agents
                await ProcessPromptInternalAsync(prompt, cancellationToken);
                
                // Only mark as completed if we're not waiting for subtasks
                if (Status != AgentStatus.WaitingForSubtasks)
                {
                    ChangeAgentStatus(AgentStatus.Completed, "Prompt processing completed successfully");
                    LogInfo("Prompt processing completed");
                }
            }
            catch (Exception ex)
            {
                ChangeAgentStatus(AgentStatus.Failed, $"Prompt processing failed: {ex.Message}");
                LogError($"Error processing prompt: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Assigns a subtask to a child agent by spawning a new agent instance.
        /// </summary>
        /// <param name="subtaskPrompt">The prompt or task description for the subtask</param>
        /// <param name="agentType">Optional specific agent type to spawn for the subtask</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task containing the ID of the spawned child agent</returns>
        public virtual async Task<string> AssignSubtaskAsync(string subtaskPrompt, string? agentType = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(subtaskPrompt))
                throw new ArgumentException("Subtask prompt cannot be null or empty", nameof(subtaskPrompt));

            if (_agentFactory == null)
                throw new InvalidOperationException("Agent factory not available. Ensure the agent was properly initialized.");

            // Check hierarchy depth limit
            if (_hierarchyDepth >= MAX_HIERARCHY_DEPTH)
            {
                LogWarning($"Maximum hierarchy depth ({MAX_HIERARCHY_DEPTH}) reached. Cannot spawn more child agents.");
                throw new InvalidOperationException($"Maximum hierarchy depth ({MAX_HIERARCHY_DEPTH}) reached");
            }

            // Check child count limit
            if (ChildAgentIds.Count >= MAX_CHILD_AGENTS)
            {
                LogWarning($"Maximum child agents ({MAX_CHILD_AGENTS}) reached. Cannot spawn more children.");
                throw new InvalidOperationException($"Maximum child agents ({MAX_CHILD_AGENTS}) reached");
            }

            LogInfo($"Assigning subtask: {subtaskPrompt}");

            try
            {
                // Spawn a child agent for the subtask
                agentType ??= "Agent"; // Default to basic Agent type
                var childAgent = await _agentFactory.SpawnAgentAsync(agentType, subtaskPrompt, Id, cancellationToken: cancellationToken);
                
                // Set the child's hierarchy depth
                if (childAgent is Agent childAgentImpl)
                {
                    childAgentImpl.SetHierarchyDepth(_hierarchyDepth + 1);
                }
                
                // Track the child agent
                lock (_lockObject)
                {
                    _childAgentIds.Add(childAgent.Id);
                }

                // Fire event
                ChildAgentSpawned?.Invoke(this, new ChildAgentSpawnedEventArgs(Id, childAgent.Id, subtaskPrompt, agentType));
                
                LogInfo($"Spawned child agent {childAgent.Id} for subtask (Depth: {_hierarchyDepth + 1})");
                return childAgent.Id;
            }
            catch (Exception ex)
            {
                LogError($"Error assigning subtask: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Handles the completion of a subtask by a child agent.
        /// </summary>
        /// <param name="childAgentId">The ID of the child agent that completed the subtask</param>
        /// <param name="result">The result or output from the completed subtask</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous subtask completion handling</returns>
        public virtual async Task HandleSubtaskCompletionAsync(string childAgentId, object result, CancellationToken cancellationToken = default)
        {
            LogInfo($"Child agent {childAgentId} completed subtask with result: {result}");
            
            // Fire event
            SubtaskCompleted?.Invoke(this, new SubtaskCompletedEventArgs(Id, childAgentId, result));
            
            // Remove from child tracking
            lock (_lockObject)
            {
                _childAgentIds.Remove(childAgentId);
            }

            // Store the result for use in ProcessSubtaskResultAsync
            _lastSubtaskResult = result;
            _subtaskResults[childAgentId] = result;
            
            LogInfo($"Stored result from child agent {childAgentId} and preparing to process next subtask");

            // Process the result (override in derived classes for specific behavior)
            await ProcessSubtaskResultAsync(childAgentId, result, cancellationToken);
            
            // Note: We don't call ProcessNextSubtaskAsync directly here anymore because
            // it's now handled within ProcessSubtaskResultAsync to avoid duplication
        }

        /// <summary>
        /// Handles the failure of a subtask by a child agent.
        /// </summary>
        /// <param name="childAgentId">The ID of the child agent that failed the subtask</param>
        /// <param name="error">The error or exception that caused the failure</param>
        /// <param name="cancellationToken">Token for cancelling the operation</param>
        /// <returns>A task representing the asynchronous subtask failure handling</returns>
        public virtual async Task HandleSubtaskFailureAsync(string childAgentId, Exception error, CancellationToken cancellationToken = default)
        {
            LogError($"Child agent {childAgentId} failed subtask: {error.Message}");
            
            // Remove from child tracking
            lock (_lockObject)
            {
                _childAgentIds.Remove(childAgentId);
            }

            // Handle the failure (override in derived classes for specific behavior)
            await ProcessSubtaskFailureAsync(childAgentId, error, cancellationToken);
        }

        /// <summary>
        /// Sets the agent factory reference during initialization.
        /// Called by the runtime when the agent is spawned.
        /// </summary>
        /// <param name="agentFactory">The agent factory instance</param>
        public virtual void SetAgentFactory(IAgentFactory agentFactory)
        {
            _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        }

        /// <summary>
        /// Sets the parent agent ID during initialization.
        /// Called by the runtime when the agent is spawned as a child.
        /// </summary>
        /// <param name="parentAgentId">The parent agent ID</param>
        public virtual void SetParentAgentId(string? parentAgentId)
        {
            _parentAgentId = parentAgentId;
        }

        /// <summary>
        /// Sets the hierarchy depth for this agent.
        /// Used to track how deep in the agent hierarchy this agent is.
        /// </summary>
        /// <param name="depth">The hierarchy depth</param>
        public virtual void SetHierarchyDepth(int depth)
        {
            _hierarchyDepth = depth;
            LogInfo($"Hierarchy depth set to: {depth}");
        }

        /// <summary>
        /// Internal method for processing prompts. Override in derived classes for specific behavior.
        /// </summary>
        /// <param name="prompt">The prompt to process</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the processing operation</returns>
        protected virtual async Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken)
        {
            LogInfo($"Processing prompt: '{prompt}' (Depth: {HierarchyDepth})");
            ChangeAgentStatus(AgentStatus.Processing, "Analyzing prompt");

            if (ShouldDecomposeTask(prompt))
            {
                ChangeAgentStatus(AgentStatus.Decomposing, "Prompt requires decomposition into subtasks");
                var subtaskPrompts = GenerateSubtasks(prompt);

                if (subtaskPrompts.Count > MAX_CHILD_AGENTS)
                {
                    LogWarning($"Generated {subtaskPrompts.Count} subtasks, which exceeds the limit of {MAX_CHILD_AGENTS}. Truncating list.");
                    subtaskPrompts = subtaskPrompts.Take(MAX_CHILD_AGENTS).ToList();
                }

                LogInfo($"Decomposed prompt into {subtaskPrompts.Count} subtasks.");
                foreach (var subtask in subtaskPrompts)
                {
                    LogInfo($"Subtask: '{subtask}'");
                }
                
                _subtaskQueue = new Queue<string>(subtaskPrompts);
                _subtaskResults.Clear();
                _lastSubtaskResult = null;
                
                ChangeAgentStatus(AgentStatus.WaitingForSubtasks, "Waiting for subtasks to complete");
                await ProcessNextSubtaskAsync(cancellationToken);
            }
            else
            {
                ChangeAgentStatus(AgentStatus.Executing, "Executing simple prompt");
                // For a simple, non-decomposed task, we could potentially use an LLM directly here
                // or decide it's a final answer. For now, we assume it's not a decomposed task.
                LogInfo("Prompt does not require decomposition. Task considered complete for this agent.");
                ChangeAgentStatus(AgentStatus.Completed, "Simple task finished");
            }
        }

        protected virtual async Task ProcessNextSubtaskAsync(CancellationToken cancellationToken)
        {
            LogInfo($"Processing next subtask. Queue count: {_subtaskQueue.Count}");
            
            if (_subtaskQueue.Count == 0)
            {
                LogInfo("Subtask queue is empty. Plan execution complete.");
                ChangeAgentStatus(AgentStatus.Completed, "All subtasks finished.");
                return;
            }

            var subtaskPrompt = _subtaskQueue.Dequeue();
            LogInfo($"Dequeued subtask: '{subtaskPrompt}'");
            
            var finalPrompt = subtaskPrompt;
            var agentType = DetermineAgentType(subtaskPrompt);
            LogInfo($"Determined agent type for subtask: {agentType}");

            // Special handling for CodeEditorTool to forward content from previous step
            if (agentType == "CodeEditorTool" && _lastSubtaskResult != null)
            {
                string content = null;
                
                // Extract content from previous result based on its type
                if (_lastSubtaskResult is string stringResult)
                {
                    content = stringResult;
                    LogInfo($"Using string result from previous step (length: {content.Length})");
                }
                else if (_lastSubtaskResult is ToolResult toolResult && toolResult.IsSuccess && toolResult.Output is string outputStr)
                {
                    content = outputStr;
                    LogInfo($"Using tool result output from previous step (length: {content.Length})");
                }
                
                if (content != null)
                {
                    // Extract path from the subtask prompt
                    var pathMatch = System.Text.RegularExpressions.Regex.Match(subtaskPrompt, @"'([^']*)'|\""([^""]*)\""");
                    var path = pathMatch.Success 
                        ? (pathMatch.Groups[1].Success ? pathMatch.Groups[1].Value : pathMatch.Groups[2].Value) 
                        : "output.txt";
                    
                    LogInfo($"Extracted path from subtask: '{path}'");
                    
                    // Escape quotes in content to prevent command injection
                    var escapedContent = content.Replace("\"", "\\\"");
                    
                    // Construct the final tool request
                    finalPrompt = $"WriteFile --path \"{path}\" --content \"{escapedContent}\"";
                    LogInfo($"Constructed CodeEditorTool command: '{finalPrompt}'");
                }
                else
                {
                    LogError($"Cannot execute CodeEditorTool task '{subtaskPrompt}' because the previous step's result could not be converted to usable content.");
                    LogError($"Previous result was of type {_lastSubtaskResult?.GetType().Name ?? "null"}");
                    ChangeAgentStatus(AgentStatus.Failed, "Missing content for file operation.");
                    return;
                }
            }
            else if (agentType == "CodeEditorTool")
            {
                LogError($"Cannot execute CodeEditorTool task '{subtaskPrompt}' because there was no previous step result.");
                ChangeAgentStatus(AgentStatus.Failed, "Missing content for file operation.");
                return;
            }

            try
            {
                LogInfo($"Spawning agent of type '{agentType}' for subtask");
                var childAgentId = await AssignSubtaskAsync(finalPrompt, agentType, cancellationToken);
                LogInfo($"Spawned child agent '{childAgentId}' for subtask");
                ChangeAgentStatus(AgentStatus.WaitingForSubtasks, $"Waiting for subtask execution by {childAgentId}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to spawn agent for subtask: {ex.Message}");
                ChangeAgentStatus(AgentStatus.Failed, $"Failed to spawn agent: {ex.Message}");
            }
        }

        protected virtual string DetermineAgentType(string subtaskPrompt)
        {
            var promptLower = subtaskPrompt.ToLower();
            
            // Check for code generation tasks
            if (promptLower.Contains("write code") || 
                promptLower.Contains("generate code") || 
                promptLower.Contains("create code") ||
                promptLower.Contains("write a hello world") ||
                promptLower.Contains("program") && (promptLower.Contains("c#") || promptLower.Contains("python") || promptLower.Contains("javascript")))
            {
                LogInfo($"Determined LLMAgent is appropriate for coding task: '{subtaskPrompt}'");
                return "LLMAgent";
            }
            
            // Check for file operations
            if (promptLower.Contains("save to a file") || 
                promptLower.Contains("write to file") || 
                promptLower.Contains("save it to") ||
                promptLower.Contains("create file") ||
                (promptLower.Contains("save") && promptLower.Contains(".cs")))
            {
                LogInfo($"Determined CodeEditorTool is appropriate for file operation: '{subtaskPrompt}'");
                return "CodeEditorTool";
            }
            
            // Default to standard agent for other types of tasks
            LogInfo($"Using default Agent for general task: '{subtaskPrompt}'");
            return "Agent";
        }

        /// <summary>
        /// Determines whether a task should be decomposed into subtasks.
        /// This prevents infinite recursion by being more selective about when to decompose.
        /// </summary>
        /// <param name="prompt">The prompt to analyze</param>
        /// <returns>True if the task should be decomposed</returns>
        protected virtual bool ShouldDecomposeTask(string prompt)
        {
            var promptLower = prompt.ToLower();
            var keywords = new[] { " and ", " then ", " after that " };
            return keywords.Any(promptLower.Contains);
        }

        /// <summary>
        /// Generates specific subtasks based on the prompt content.
        /// This creates more targeted subtasks to avoid infinite recursion.
        /// </summary>
        /// <param name="prompt">The original prompt</param>
        /// <returns>List of specific subtasks</returns>
        protected virtual List<string> GenerateSubtasks(string prompt)
        {
            var subtasks = new List<string>();
            var promptLower = prompt.ToLower();
            
            // Check for complex tasks that involve coding and saving
            if ((promptLower.Contains("write") || promptLower.Contains("create") || promptLower.Contains("generate")) 
                && promptLower.Contains("save"))
            {
                // Split into code generation and file saving tasks
                string codeTask = null;
                string saveTask = null;
                
                if (promptLower.Contains("hello world") && promptLower.Contains("c#"))
                {
                    codeTask = "write a hello world c# console application";
                    
                    // Extract filename from the prompt
                    if (promptLower.Contains("program.cs"))
                    {
                        saveTask = "save it to a file named 'program.cs'";
                    }
                    else if (promptLower.Contains(".cs"))
                    {
                        // Try to extract the filename using regex
                        var match = System.Text.RegularExpressions.Regex.Match(prompt, @"['""]([^'""]+\.cs)['""]");
                        var filename = match.Success ? match.Groups[1].Value : "output.cs";
                        saveTask = $"save it to a file named '{filename}'";
                    }
                    else
                    {
                        saveTask = "save it to a file named 'program.cs'";
                    }
                }
                else
                {
                    // Generic decomposition
                    int saveIndex = promptLower.IndexOf("save");
                    if (saveIndex > 10) // Ensure we have enough content for a meaningful split
                    {
                        codeTask = prompt.Substring(0, saveIndex).Trim();
                        saveTask = prompt.Substring(saveIndex).Trim();
                    }
                    else
                    {
                        // Fallback to simple splitting by "and"
                        var andParts = prompt.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries);
                        if (andParts.Length > 1)
                        {
                            codeTask = andParts[0].Trim();
                            saveTask = andParts[1].Trim();
                        }
                        else
                        {
                            // Can't decompose, just use the original prompt
                            subtasks.Add(prompt);
                            return subtasks;
                        }
                    }
                }
                
                if (codeTask != null) subtasks.Add(codeTask);
                if (saveTask != null) subtasks.Add(saveTask);
                
                return subtasks;
            }
            
            // Default decomposition logic for other types of tasks
            var separatorParts = prompt.Split(new[] { " and ", " then ", ". " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in separatorParts)
            {
                subtasks.Add(part.Trim());
            }
            
            return subtasks;
        }

        /// <summary>
        /// Processes the result from a completed subtask. Override in derived classes for specific behavior.
        /// </summary>
        /// <param name="childAgentId">The child agent ID</param>
        /// <param name="result">The subtask result</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the processing operation</returns>
        protected virtual async Task ProcessSubtaskResultAsync(string childAgentId, object result, CancellationToken cancellationToken)
        {
            LogInfo($"Processing result from completed subtask '{childAgentId}'. Result type: {result?.GetType().Name ?? "null"}");
            
            // Store the result for later use (already done in HandleSubtaskCompletionAsync)
            
            if (result is ToolResult toolResult)
            {
                if (toolResult.IsSuccess)
                {
                    LogInfo($"Subtask {childAgentId} completed successfully with tool result: {toolResult.Output}");
                    _lastSubtaskResult = toolResult.Output;
                }
                else
                {
                    LogError($"Subtask {childAgentId} failed with tool result error: {toolResult.Error}");
                    _lastSubtaskResult = toolResult.Error;
                    ChangeAgentStatus(AgentStatus.Failed, $"Subtask {childAgentId} failed: {toolResult.Error}");
                }
            }
            else if (result is string stringResult)
            {
                LogInfo($"Subtask {childAgentId} completed with string result (length: {stringResult.Length})");
                _lastSubtaskResult = stringResult;
            }
            else
            {
                LogInfo($"Subtask {childAgentId} completed with result of type {result?.GetType().Name ?? "null"}");
                _lastSubtaskResult = result;
            }
            
            // Continue to the next subtask if available
            if (_subtaskQueue.Count > 0)
            {
                LogInfo($"Queue has {_subtaskQueue.Count} more subtasks. Processing next one from ProcessSubtaskResultAsync.");
                await ProcessNextSubtaskAsync(cancellationToken);
            }
            else if (_childAgentIds.Count == 0)
            {
                // All subtasks are complete
                LogInfo("All subtasks completed from ProcessSubtaskResultAsync.");
                ChangeAgentStatus(AgentStatus.Completed, "All subtasks completed successfully");
            }
            else
            {
                LogInfo($"Waiting for {_childAgentIds.Count} child agents to complete.");
                ChangeAgentStatus(AgentStatus.WaitingForSubtasks, $"Waiting for {_childAgentIds.Count} child agents to complete");
            }
        }

        /// <summary>
        /// Processes a subtask failure. Override in derived classes for specific behavior.
        /// </summary>
        /// <param name="childAgentId">The child agent ID</param>
        /// <param name="error">The failure error</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the processing operation</returns>
        protected virtual async Task ProcessSubtaskFailureAsync(string childAgentId, Exception error, CancellationToken cancellationToken)
        {
            LogError($"Handling failure from child {childAgentId}: {error.Message}");
            
            // For basic implementation, mark as failed if any subtask fails
            ChangeAgentStatus(AgentStatus.Failed, $"Subtask failed: {error.Message}");
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Handles status request messages.
        /// </summary>
        private async Task HandleStatusRequestAsync(GetAgentStatusMessage statusMsg, IMessageEnvelope originalEnvelope, CancellationToken cancellationToken)
        {
            LogInfo($"Handling status request from: {originalEnvelope.Headers?.FirstOrDefault(h => h.Key == "SenderId").Value ?? "unknown"}");

            string? originalSenderId = null;
            if (originalEnvelope.Headers?.TryGetValue("SenderId", out var sid) == true)
            {
                originalSenderId = sid;
            }

            if (string.IsNullOrEmpty(originalSenderId))
            {
                LogWarning("Cannot respond to status request: SenderId not found in original envelope headers.");
                return;
            }

            if (_agentFactory?.RuntimeAdapter == null)
            {
                LogError("Cannot send status response: AgentFactory or RuntimeAdapter is not available.");
                return;
            }

            var responsePayload = new 
            {
                Status,
                CurrentPrompt,
                ChildAgentIds = ChildAgentIds.ToList(),
                AgentId = Id
            };
            
            var mcpMetadata = new Dictionary<string, object> { { "Timestamp", DateTimeOffset.UtcNow } };
            if (originalEnvelope.Metadata?.TryGetValue("CorrelationId", out var corrId) == true)
            {
                mcpMetadata["CorrelationId"] = corrId; // Echo correlation ID
            }

            var mcpHeaders = new Dictionary<string, string>
            {
                { "SenderId", Id }, // This agent is the sender of the response
                { "ReceiverId", originalSenderId }, // Send back to original sender
                { "MessageType", responsePayload.GetType().Name },
                { "Version", "1.0" }
            };

            try
            {
                await _agentFactory.RuntimeAdapter.SendMessageAsync(originalSenderId, responsePayload, Id, mcpHeaders, cancellationToken);
                LogInfo($"Successfully sent status response to {originalSenderId}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to send status response to {originalSenderId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Changes the actor state and fires the StateChanged event.
        /// </summary>
        private void ChangeActorState(ActorState newState, string? reason = null)
        {
            var previousState = _actorState;
            _actorState = newState;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previousState, newState, reason));
        }

        /// <summary>
        /// Changes the agent status and fires the StatusChanged event.
        /// </summary>
        protected void ChangeAgentStatus(AgentStatus newStatus, string? reason = null)
        {
            var previousStatus = _status;
            _status = newStatus;
            StatusChanged?.Invoke(this, new AgentStatusChangedEventArgs(previousStatus, newStatus, reason));
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        protected virtual void LogInfo(string message)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [INFO] Agent {Id} (D{_hierarchyDepth}): {message}");
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        protected virtual void LogWarning(string message)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [WARN] Agent {Id} (D{_hierarchyDepth}): {message}");
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        protected virtual void LogError(string message)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [ERROR] Agent {Id} (D{_hierarchyDepth}): {message}");
        }
    }
} 