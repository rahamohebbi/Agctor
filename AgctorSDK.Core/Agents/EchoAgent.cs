using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.Logging;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// A simple agent that echoes received messages and is useful for demonstration purposes.
    /// </summary>
    public class EchoAgent : IAgent
    {
        private readonly IAgctorLogger _logger;
        private AgentStatus _status;
        private ActorState _state;
        private readonly List<string> _childAgentIds;
        
        /// <summary>
        /// Initializes a new instance of the EchoAgent class.
        /// </summary>
        /// <param name="id">The agent ID.</param>
        public EchoAgent(string id)
        {
            Id = id;
            _status = AgentStatus.Idle;
            _state = ActorState.Initializing;
            _childAgentIds = new List<string>();
            _logger = LoggerFactory.CreateLogger($"EchoAgent:{id}");
        }

        /// <inheritdoc/>
        public string Id { get; }

        /// <inheritdoc/>
        public string ActorType => "EchoAgent";

        /// <inheritdoc/>
        public string AgentType => ActorType;

        /// <inheritdoc/>
        public ActorState State => _state;

        /// <inheritdoc/>
        public AgentStatus Status => _status;

        /// <inheritdoc/>
        public string? CurrentPrompt { get; private set; }

        /// <inheritdoc/>
        public string? ParentAgentId { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<string> ChildAgentIds => _childAgentIds.AsReadOnly();

        /// <inheritdoc/>
        public event EventHandler<AgentStatusChangedEventArgs>? StatusChanged;

        /// <inheritdoc/>
        public event EventHandler<ChildAgentSpawnedEventArgs>? ChildAgentSpawned;

        /// <inheritdoc/>
        public event EventHandler<SubtaskCompletedEventArgs>? SubtaskCompleted;

        /// <inheritdoc/>
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

        /// <inheritdoc/>
        public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _logger.Info($"Agent {Id} received message via ReceiveAsync: {envelope.Payload}");
            return Task.FromResult(envelope);
        }

        /// <inheritdoc/>
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var previousState = _state;
            _state = ActorState.Active;
            _logger.Info($"Agent {Id} initialized");
            OnStateChanged(new ActorStateChangedEventArgs(previousState, _state));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            var previousState = _state;
            _state = ActorState.Stopped;
            _logger.Info($"Agent {Id} shut down");
            OnStateChanged(new ActorStateChangedEventArgs(previousState, _state));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ActivateAsync()
        {
            _logger.Info($"Agent {Id} activated");
            UpdateStatus(AgentStatus.Idle);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DeactivateAsync()
        {
            _logger.Info($"Agent {Id} deactivated");
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task ProcessMessageAsync(IMessageEnvelope envelope)
        {
            UpdateStatus(AgentStatus.Working);
            
            _logger.Info($"Agent {Id} received message: {envelope.Payload}");
            
            // Simulate some processing time
            await Task.Delay(200);
            
            // Echo the message
            var response = $"Echo from {Id}: {envelope.Payload}";
            _logger.Info($"Agent {Id} processed message: {response}");
            
            UpdateStatus(AgentStatus.Completed);
        }

        /// <inheritdoc/>
        public void UpdateStatus(AgentStatus newStatus)
        {
            var oldStatus = _status;
            _status = newStatus;
            _logger.Debug($"Agent {Id} status changed: {oldStatus} -> {newStatus}");
            
            OnStatusChanged(new AgentStatusChangedEventArgs(oldStatus, newStatus));
        }

        /// <inheritdoc/>
        public Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            CurrentPrompt = prompt;
            UpdateStatus(AgentStatus.Working);
            
            _logger.Info($"Agent {Id} processing prompt: {prompt}");
            
            // For demo purposes, just complete the prompt immediately
            UpdateStatus(AgentStatus.Completed);
            
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<string> AssignSubtaskAsync(string subtaskPrompt, string? agentType = null, CancellationToken cancellationToken = default)
        {
            _logger.Info($"Agent {Id} attempting to assign subtask: {subtaskPrompt}");
            
            // This is a simple agent with no ability to create subtasks
            throw new NotImplementedException("EchoAgent cannot assign subtasks");
        }

        /// <inheritdoc/>
        public Task HandleSubtaskCompletionAsync(string childAgentId, object result, CancellationToken cancellationToken = default)
        {
            _logger.Info($"Agent {Id} handling subtask completion from {childAgentId}");
            
            // This is a simple agent with no ability to handle subtasks
            throw new NotImplementedException("EchoAgent cannot handle subtask completion");
        }

        /// <inheritdoc/>
        public Task HandleSubtaskFailureAsync(string childAgentId, Exception error, CancellationToken cancellationToken = default)
        {
            _logger.Info($"Agent {Id} handling subtask failure from {childAgentId}: {error.Message}");
            
            // This is a simple agent with no ability to handle subtasks
            throw new NotImplementedException("EchoAgent cannot handle subtask failure");
        }

        /// <summary>
        /// Raises the StatusChanged event.
        /// </summary>
        protected virtual void OnStatusChanged(AgentStatusChangedEventArgs e)
        {
            StatusChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the ChildAgentSpawned event.
        /// </summary>
        protected virtual void OnChildAgentSpawned(ChildAgentSpawnedEventArgs e)
        {
            ChildAgentSpawned?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the SubtaskCompleted event.
        /// </summary>
        protected virtual void OnSubtaskCompleted(SubtaskCompletedEventArgs e)
        {
            SubtaskCompleted?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the StateChanged event.
        /// </summary>
        protected virtual void OnStateChanged(ActorStateChangedEventArgs e)
        {
            StateChanged?.Invoke(this, e);
        }
    }
} 