using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Utils.ActivityTracking;

namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Decorator for IAgentFactory that automatically adds activity tracking to created agents.
    /// </summary>
    public class TracingAgentFactory : IAgentFactory
    {
        private readonly IAgentFactory _innerFactory;
        private readonly IActivityTracker _activityTracker;
        
        /// <summary>
        /// Initializes a new instance of the TracingAgentFactory class.
        /// </summary>
        /// <param name="innerFactory">The factory being decorated.</param>
        /// <param name="activityTracker">The activity tracker to use for tracing.</param>
        public TracingAgentFactory(IAgentFactory innerFactory, IActivityTracker activityTracker)
        {
            _innerFactory = innerFactory;
            _activityTracker = activityTracker;
        }

        /// <inheritdoc/>
        public IActorRuntimeAdapter RuntimeAdapter => _innerFactory.RuntimeAdapter;

        /// <inheritdoc/>
        public async Task<TAgent> SpawnAgentAsync<TAgent>(string prompt, string? parentAgentId = null, string? agentId = null, CancellationToken cancellationToken = default) where TAgent : class, IAgent
        {
            using var activity = _activityTracker.StartActivity("AgentFactory.SpawnAgent");
            activity.SetAttribute("agent.type", typeof(TAgent).Name);
            if (agentId != null)
            {
                activity.SetAttribute("agent.id", agentId);
            }
            if (parentAgentId != null)
            {
                activity.SetAttribute("agent.parent_id", parentAgentId);
            }
            activity.SetAttribute("prompt", prompt);
            
            try
            {
                var agent = await _innerFactory.SpawnAgentAsync<TAgent>(prompt, parentAgentId, agentId, cancellationToken);
                
                // Only decorate if not already a TracedAgent
                if (!(agent is TracedAgent))
                {
                    return new TracedAgent(agent, _activityTracker) as TAgent;
                }
                
                activity.SetStatus(ActivityStatus.Ok);
                return agent;
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<IAgent> SpawnAgentAsync(string agentTypeName, string prompt, string? parentAgentId = null, string? agentId = null, CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("AgentFactory.SpawnAgentByType");
            activity.SetAttribute("agent.type", agentTypeName);
            if (agentId != null)
            {
                activity.SetAttribute("agent.id", agentId);
            }
            if (parentAgentId != null)
            {
                activity.SetAttribute("agent.parent_id", parentAgentId);
            }
            activity.SetAttribute("prompt", prompt);
            
            try
            {
                var agent = await _innerFactory.SpawnAgentAsync(agentTypeName, prompt, parentAgentId, agentId, cancellationToken);
                
                // Only decorate if not already a TracedAgent
                if (!(agent is TracedAgent))
                {
                    agent = new TracedAgent(agent, _activityTracker);
                }
                
                activity.SetStatus(ActivityStatus.Ok);
                return agent;
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<TAgent?> GetAgentAsync<TAgent>(string agentId, CancellationToken cancellationToken = default) where TAgent : class, IAgent
        {
            using var activity = _activityTracker.StartActivity("AgentFactory.GetAgent");
            activity.SetAttribute("agent.id", agentId);
            activity.SetAttribute("agent.requested_type", typeof(TAgent).Name);
            
            try
            {
                var agent = await _innerFactory.GetAgentAsync<TAgent>(agentId, cancellationToken);
                
                if (agent != null && !(agent is TracedAgent))
                {
                    return new TracedAgent(agent, _activityTracker) as TAgent;
                }
                
                activity.SetStatus(ActivityStatus.Ok);
                return agent;
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<IAgent?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("AgentFactory.GetAgent");
            activity.SetAttribute("agent.id", agentId);
            
            try
            {
                var agent = await _innerFactory.GetAgentAsync(agentId, cancellationToken);
                
                if (agent != null && !(agent is TracedAgent))
                {
                    agent = new TracedAgent(agent, _activityTracker);
                }
                
                activity.SetStatus(ActivityStatus.Ok);
                return agent;
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task StopAgentAsync(string agentId, CancellationToken cancellationToken = default)
        {
            using var activity = _activityTracker.StartActivity("AgentFactory.StopAgent");
            activity.SetAttribute("agent.id", agentId);
            
            try
            {
                await _innerFactory.StopAgentAsync(agentId, cancellationToken);
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
        public string GenerateAgentId(string agentTypeName, string? parentAgentId = null)
        {
            using var activity = _activityTracker.StartActivity("AgentFactory.GenerateAgentId");
            activity.SetAttribute("agent.type", agentTypeName);
            if (parentAgentId != null)
            {
                activity.SetAttribute("agent.parent_id", parentAgentId);
            }
            
            try
            {
                var agentId = _innerFactory.GenerateAgentId(agentTypeName, parentAgentId);
                activity.SetAttribute("agent.generated_id", agentId);
                activity.SetStatus(ActivityStatus.Ok);
                return agentId;
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public void RegisterToolActorType<T>(string? typeName = null) where T : class, IActor, IToolActor =>
            _innerFactory.RegisterToolActorType<T>(typeName);

        /// <inheritdoc/>
        public IReadOnlyCollection<string> GetRegisteredToolActorTypeNames() =>
            _innerFactory.GetRegisteredToolActorTypeNames();

        /// <inheritdoc/>
        public bool IsToolActorType(string typeName) => _innerFactory.IsToolActorType(typeName);

        /// <inheritdoc/>
        public async Task<ToolResult> InvokeToolByPromptAsync(
            string toolTypeName,
            string prompt,
            string? invokingAgentId = null,
            CancellationToken cancellationToken = default)
        {
            var request = new ToolRequest
            {
                Operation = "Prompt",
                Parameters = new Dictionary<string, object> { ["prompt"] = prompt ?? "" }
            };
            return await InvokeToolRequestAsync(toolTypeName, request, invokingAgentId, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ToolResult> InvokeToolRequestAsync(
            string toolTypeName,
            ToolRequest request,
            string? invokingAgentId = null,
            CancellationToken cancellationToken = default)
        {
            var op = string.IsNullOrWhiteSpace(request.Operation) ? "Handle" : request.Operation.Trim();
            using var activity = _activityTracker.StartActivity($"tool.{ToolTraceTimelineDetail.FriendlyToolLabel(toolTypeName)}");
            activity.SetAttribute("display.name", ToolTraceTimelineDetail.FormatDisplayName(toolTypeName, op));
            activity.SetAttribute("tool.id", toolTypeName);
            activity.SetAttribute("tool.operation", op);
            if (!string.IsNullOrWhiteSpace(invokingAgentId))
                activity.SetAttribute("tool.invoking_agent", invokingAgentId);

            try
            {
                var result = await _innerFactory
                    .InvokeToolRequestAsync(toolTypeName, request, invokingAgentId, cancellationToken)
                    .ConfigureAwait(false);
                activity.SetTimelineDetailJson(
                    ToolTraceTimelineDetail.BuildInvokeJson(
                        toolTypeName,
                        op,
                        request.Parameters,
                        result,
                        invokingAgentId));
                activity.SetStatus(
                    result.IsSuccess ? ActivityStatus.Ok : ActivityStatus.Error,
                    result.IsSuccess ? null : result.Error);
                return result;
            }
            catch (Exception ex)
            {
                activity.SetTimelineDetailJson(
                    ToolTraceTimelineDetail.BuildInvokeJson(
                        toolTypeName,
                        op,
                        request.Parameters,
                        result: null,
                        invokingAgentId,
                        exception: ex));
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public IAgent CreateAgent(string agentType, string id, AgentOptions options)
        {
            using var activity = _activityTracker.StartActivity("AgentFactory.CreateAgent");
            activity.SetAttribute("agent.type", agentType);
            activity.SetAttribute("agent.id", id);

            try
            {
                // This is a legacy method that isn't in the IAgentFactory interface
                // We're implementing it for compatibility with demo code
                if (_innerFactory is IDynamicObject dynamicFactory &&
                    dynamicFactory.TryInvokeMember("CreateAgent", new object[] { agentType, id, options }, out var result) &&
                    result is IAgent agent)
                {
                    // Decorate the agent with tracing
                    var tracedAgent = new TracedAgent(agent, _activityTracker);

                    activity.SetStatus(ActivityStatus.Ok);
                    return tracedAgent;
                }

                throw new NotImplementedException($"The inner factory does not support the CreateAgent method");
            }
            catch (Exception ex)
            {
                activity.SetStatus(ActivityStatus.Error);
                activity.RecordException(ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Interface for dynamic method invocation.
    /// </summary>
    public interface IDynamicObject
    {
        /// <summary>
        /// Tries to invoke a member method on the object.
        /// </summary>
        /// <param name="name">The name of the method.</param>
        /// <param name="args">The arguments to pass to the method.</param>
        /// <param name="result">The result of the method invocation.</param>
        /// <returns>True if the method was successfully invoked; otherwise, false.</returns>
        bool TryInvokeMember(string name, object[] args, out object? result);
    }
} 