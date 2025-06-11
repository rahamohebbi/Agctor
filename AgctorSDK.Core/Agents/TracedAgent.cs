using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Utils.ActivityTracking;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Decorator for IAgent that adds activity tracking capabilities.
    /// This allows tracing agent operations without modifying the core agent implementations.
    /// </summary>
    public class TracedAgent : IAgent
    {
        private readonly IAgent _innerAgent;
        private readonly IActivityTracker _activityTracker;
        
        /// <summary>
        /// Initializes a new instance of the TracedAgent class.
        /// </summary>
        /// <param name="innerAgent">The agent being decorated.</param>
        /// <param name="activityTracker">The activity tracker to use for tracing.</param>
        public TracedAgent(IAgent innerAgent, IActivityTracker activityTracker)
        {
            _innerAgent = innerAgent;
            _activityTracker = activityTracker;
        }
        
        /// <inheritdoc/>
        public string Id => _innerAgent.Id;
        
        /// <inheritdoc/>
        public AgentStatus Status => _innerAgent.Status;
        
        /// <inheritdoc/>
        public string ActorType => _innerAgent.ActorType;

        /// <inheritdoc/>
        public ActorState State => _innerAgent.State;

        /// <inheritdoc/>
        public string? CurrentPrompt => _innerAgent.CurrentPrompt;

        /// <inheritdoc/>
        public string? ParentAgentId => _innerAgent.ParentAgentId;

        /// <inheritdoc/>
        public IReadOnlyList<string> ChildAgentIds => _innerAgent.ChildAgentIds;
        
        /// <inheritdoc/>
        public string? Name => _innerAgent.Name;
        
        /// <inheritdoc/>
        public string? Description => $"Traced: {_innerAgent.Description}";
        
        /// <inheritdoc/>
        public void SetAgentFactory(IAgentFactory agentFactory)
        {
            using var activity = _activityTracker.StartActivity("Agent.SetAgentFactory");
            activity.SetAttribute("agent.id", Id);
            activity.SetAttribute("agent.type", ActorType);
            
            try
            {
                _innerAgent.SetAgentFactory(agentFactory);
                activity.SetStatus(ActivityStatus.Ok);
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }
        
        /// <inheritdoc/>
        public void SetParentAgentId(string? parentAgentId)
        {
            using var activity = _activityTracker.StartActivity("Agent.SetParentAgentId");
            activity.SetAttribute("agent.id", Id);
            activity.SetAttribute("agent.type", ActorType);
            activity.SetAttribute("parent.id", parentAgentId ?? "null");
            
            try
            {
                _innerAgent.SetParentAgentId(parentAgentId);
                activity.SetStatus(ActivityStatus.Ok);
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public event EventHandler<AgentStatusChangedEventArgs>? StatusChanged
        {
            add => _innerAgent.StatusChanged += value;
            remove => _innerAgent.StatusChanged -= value;
        }

        /// <inheritdoc/>
        public event EventHandler<ChildAgentSpawnedEventArgs>? ChildAgentSpawned
        {
            add => _innerAgent.ChildAgentSpawned += value;
            remove => _innerAgent.ChildAgentSpawned -= value;
        }

        /// <inheritdoc/>
        public event EventHandler<SubtaskCompletedEventArgs>? SubtaskCompleted
        {
            add => _innerAgent.SubtaskCompleted += value;
            remove => _innerAgent.SubtaskCompleted -= value;
        }

        /// <inheritdoc/>
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged
        {
            add => _innerAgent.StateChanged += value;
            remove => _innerAgent.StateChanged -= value;
        }

        /// <inheritdoc/>
        public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity(
                $"Agent.ReceiveAsync",
                envelope.ExtractActivityContext() // Extract parent context if present
            );
            
            activity.SetAttribute("agent.id", Id);
            activity.SetAttribute("agent.type", ActorType);
            activity.SetAttribute("agent.status", Status.ToString());
            activity.SetAttribute("message.id", envelope.Id);
            activity.SetAttribute("message.type", envelope.PayloadType());
            
            try
            {
                var result = await _innerAgent.ReceiveAsync(envelope, cancellationToken);
                activity.SetStatus(ActivityStatus.Ok);
                return result;
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("Agent.Initialize");
            activity.SetAttribute("agent.id", Id);
            activity.SetAttribute("agent.type", ActorType);
            
            try
            {
                await _innerAgent.InitializeAsync(cancellationToken);
                activity.SetStatus(ActivityStatus.Ok);
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("Agent.Shutdown");
            activity.SetAttribute("agent.id", Id);
            activity.SetAttribute("agent.type", ActorType);
            
            try
            {
                await _innerAgent.ShutdownAsync(cancellationToken);
                activity.SetStatus(ActivityStatus.Ok);
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("Agent.ProcessPrompt");
            activity.SetAttribute("agent.id", Id);
            activity.SetAttribute("agent.type", ActorType);
            activity.SetAttribute("prompt", prompt);
            
            try
            {
                await _innerAgent.ProcessPromptAsync(prompt, cancellationToken);
                activity.SetStatus(ActivityStatus.Ok);
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<string> AssignSubtaskAsync(string subtaskPrompt, string? agentType = null, CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("Agent.AssignSubtask");
            activity.SetAttribute("agent.id", Id);
            activity.SetAttribute("agent.type", ActorType);
            activity.SetAttribute("subtask.prompt", subtaskPrompt);
            if (agentType != null)
            {
                activity.SetAttribute("subtask.agent_type", agentType);
            }
            
            try
            {
                var childAgentId = await _innerAgent.AssignSubtaskAsync(subtaskPrompt, agentType, cancellationToken);
                activity.SetAttribute("subtask.child_agent_id", childAgentId);
                activity.SetStatus(ActivityStatus.Ok);
                return childAgentId;
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task HandleSubtaskCompletionAsync(string childAgentId, object result, CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("Agent.HandleSubtaskCompletion");
            activity.SetAttribute("agent.id", Id);
            activity.SetAttribute("agent.type", ActorType);
            activity.SetAttribute("subtask.child_agent_id", childAgentId);
            activity.SetAttribute("subtask.result_type", result?.GetType().Name ?? "null");
            
            try
            {
                await _innerAgent.HandleSubtaskCompletionAsync(childAgentId, result, cancellationToken);
                activity.SetStatus(ActivityStatus.Ok);
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task HandleSubtaskFailureAsync(string childAgentId, Exception error, CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("Agent.HandleSubtaskFailure");
            activity.SetAttribute("agent.id", Id);
            activity.SetAttribute("agent.type", ActorType);
            activity.SetAttribute("subtask.child_agent_id", childAgentId);
            activity.SetAttribute("subtask.error_type", error.GetType().Name);
            activity.SetAttribute("subtask.error_message", error.Message);
            
            try
            {
                await _innerAgent.HandleSubtaskFailureAsync(childAgentId, error, cancellationToken);
                activity.SetStatus(ActivityStatus.Ok);
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }
    }
} 