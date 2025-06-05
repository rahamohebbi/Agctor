using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

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
            if (envelope?.Payload == null)
            {
                LogWarning("Received null or empty message envelope");
                // Need to return a valid IMessageEnvelope, even for an error or empty case.
                // Creating a simple response or rethrowing might be options.
                // For now, returning the original envelope if it's not null, or an error envelope.
                return envelope ?? new MessageEnvelope("Error: Null envelope received", new DefaultMessageMetadata(Id, "unknown"));
            }

            LogInfo($"Received message: {envelope.Payload.GetType().Name}");

            try
            {
                // Handle different message types
                switch (envelope.Payload)
                {
                    case ProcessPromptMessage promptMsg:
                        await ProcessPromptAsync(promptMsg.Prompt, cancellationToken);
                        break;

                    case SubtaskCompletedMessage completedMsg:
                        await HandleSubtaskCompletionAsync(completedMsg.ChildAgentId, completedMsg.Result, cancellationToken);
                        break;

                    case SubtaskFailedMessage failedMsg:
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
                LogError($"Error processing message {envelope.Payload.GetType().Name}: {ex.Message}");
                // Potentially return an error envelope
                return new MessageEnvelope($"Error processing message: {ex.Message}", envelope.Id, new DefaultMessageMetadata(Id, envelope.Metadata?.SenderId ?? "unknown"));
                // Or rethrow if the interface/contract allows exceptions here to propagate.
                // For now, returning an error envelope to satisfy Task<IMessageEnvelope>.
            }
            // Return the original envelope as a default acknowledgment if no specific response was generated and no error occurred.
            return envelope;
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
            ChangeAgentStatus(AgentStatus.Working, $"Processing prompt: {prompt}");

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

            // Process the result (override in derived classes for specific behavior)
            await ProcessSubtaskResultAsync(childAgentId, result, cancellationToken);
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
            // Basic implementation: simulate some work
            LogInfo("Analyzing prompt for potential subtasks...");
            await Task.Delay(100, cancellationToken); // Simulate processing time
            
            // Only decompose tasks if we're not at maximum depth and this is a complex task
            if (_hierarchyDepth < MAX_HIERARCHY_DEPTH && ShouldDecomposeTask(prompt))
            {
                LogInfo("Detected complex task requiring subtasks");
                
                try
                {
                    // Create specific subtasks based on the prompt content
                    var subtasks = GenerateSubtasks(prompt);
                    
                    if (subtasks.Count > 0)
                    {
                        LogInfo($"Generated {subtasks.Count} subtasks");
                        
                        // Spawn child agents for each subtask
                        foreach (var subtask in subtasks)
                        {
                            await AssignSubtaskAsync(subtask, cancellationToken: cancellationToken);
                        }
                        
                        ChangeAgentStatus(AgentStatus.WaitingForSubtasks, "Waiting for child agents to complete subtasks");
                    }
                    else
                    {
                        LogInfo("No subtasks generated, completing directly");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error during task decomposition: {ex.Message}");
                    // Continue with direct processing if decomposition fails
                }
            }
            else
            {
                if (_hierarchyDepth >= MAX_HIERARCHY_DEPTH)
                {
                    LogInfo($"Maximum depth ({MAX_HIERARCHY_DEPTH}) reached, processing directly");
                }
                else
                {
                    LogInfo("Simple task completed directly");
                }
            }
        }

        /// <summary>
        /// Determines whether a task should be decomposed into subtasks.
        /// This prevents infinite recursion by being more selective about when to decompose.
        /// </summary>
        /// <param name="prompt">The prompt to analyze</param>
        /// <returns>True if the task should be decomposed</returns>
        protected virtual bool ShouldDecomposeTask(string prompt)
        {
            var lowerPrompt = prompt.ToLowerInvariant();
            
            // Only decompose if this is a root agent (depth 0) and the prompt is complex
            if (_hierarchyDepth > 0)
            {
                return false; // Child agents don't decompose further
            }
            
            // Check for complex task indicators
            var complexityIndicators = new[]
            {
                "analyze and report",
                "comprehensive analysis",
                "detailed study",
                "full investigation",
                "complete assessment"
            };
            
            return complexityIndicators.Any(indicator => lowerPrompt.Contains(indicator));
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
            var lowerPrompt = prompt.ToLowerInvariant();
            
            // Generate specific, non-recursive subtasks
            if (lowerPrompt.Contains("market trends"))
            {
                subtasks.Add("Collect current market data");
                subtasks.Add("Identify key trend indicators");
                subtasks.Add("Compile trend analysis summary");
            }
            else if (lowerPrompt.Contains("comprehensive report"))
            {
                subtasks.Add("Gather required data sources");
                subtasks.Add("Perform data analysis");
                subtasks.Add("Format final report");
            }
            else if (lowerPrompt.Contains("analyze") && lowerPrompt.Contains("report"))
            {
                // Generic analysis and reporting
                subtasks.Add("Data collection phase");
                subtasks.Add("Analysis execution");
                subtasks.Add("Report generation");
            }
            
            // Limit the number of subtasks
            return subtasks.Take(3).ToList();
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
            LogInfo($"Processing result from child {childAgentId}: {result}");
            
            // Check if all subtasks are complete
            if (ChildAgentIds.Count == 0 && Status == AgentStatus.WaitingForSubtasks)
            {
                ChangeAgentStatus(AgentStatus.Completed, "All subtasks completed successfully");
                LogInfo("All subtasks completed, agent work finished");
            }
            
            await Task.CompletedTask;
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
        private async Task HandleStatusRequestAsync(GetAgentStatusMessage statusMsg, IMessageEnvelope envelope, CancellationToken cancellationToken)
        {
            var response = new AgentStatusResponse(
                Id, 
                Status, 
                CurrentPrompt, 
                ChildAgentIds.Count, 
                $"Agent {Id} is {Status} (Depth: {_hierarchyDepth})"
            );

            // In a real implementation, we would send this response back to the requesting agent
            LogInfo($"Status requested by {statusMsg.RequestingAgentId}: {Status}");
            await Task.CompletedTask;
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