using System;
using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Timeout
{
    /// <summary>
    /// Simple timeout policy that applies a fixed timeout duration regardless of context.
    /// Useful for basic timeout management without complex logic.
    /// </summary>
    public class FixedTimeoutPolicy : ITimeoutPolicy
    {
        private readonly TimeSpan _timeout;
        private readonly bool _allowReschedule;

        public FixedTimeoutPolicy(TimeSpan timeout, bool allowReschedule = false)
        {
            _timeout = timeout;
            _allowReschedule = allowReschedule;
        }

        public TimeSpan GetTimeout(AgentContext context)
        {
            return _timeout;
        }

        public bool ShouldReschedule(AgentContext context, AgentProgress progress)
        {
            return _allowReschedule && progress.IsActivelyProgressing;
        }

        public bool ShouldAbort(AgentContext context, ActorState state)
        {
            return state == ActorState.Faulted || state == ActorState.Stopped;
        }
    }

    /// <summary>
    /// Adaptive timeout policy that adjusts timeout based on task complexity and agent type.
    /// Provides more intelligent timeout management for different scenarios.
    /// </summary>
    public class AdaptiveTimeoutPolicy : ITimeoutPolicy
    {
        private readonly TimeSpan _baseTimeout;
        private readonly double _complexityMultiplier;
        private readonly double _childAgentMultiplier;
        private readonly TimeSpan _maxTimeout;
        private readonly double _progressThreshold;

        public AdaptiveTimeoutPolicy(
            TimeSpan baseTimeout,
            double complexityMultiplier = 1.5,
            double childAgentMultiplier = 1.2,
            TimeSpan? maxTimeout = null,
            double progressThreshold = 0.1)
        {
            _baseTimeout = baseTimeout;
            _complexityMultiplier = Math.Max(1.0, complexityMultiplier);
            _childAgentMultiplier = Math.Max(1.0, childAgentMultiplier);
            _maxTimeout = maxTimeout ?? TimeSpan.FromHours(1);
            _progressThreshold = Math.Max(0.0, Math.Min(1.0, progressThreshold));
        }

        public TimeSpan GetTimeout(AgentContext context)
        {
            var timeout = _baseTimeout;

            // Adjust for task complexity
            if (context.TaskComplexity > 1)
            {
                timeout = TimeSpan.FromMilliseconds(timeout.TotalMilliseconds * Math.Pow(_complexityMultiplier, context.TaskComplexity - 1));
            }

            // Adjust for child agents (more complex operations need more time)
            if (context.ChildAgentCount > 0)
            {
                timeout = TimeSpan.FromMilliseconds(timeout.TotalMilliseconds * Math.Pow(_childAgentMultiplier, context.ChildAgentCount));
            }

            // Use budget if it's more restrictive
            if (context.TimeoutBudget.HasValue && context.TimeoutBudget.Value < timeout)
            {
                timeout = context.TimeoutBudget.Value;
            }

            // Ensure we don't exceed maximum timeout
            if (timeout > _maxTimeout)
            {
                timeout = _maxTimeout;
            }

            return timeout;
        }

        public bool ShouldReschedule(AgentContext context, AgentProgress progress)
        {
            // Reschedule if making meaningful progress
            if (progress.IsActivelyProgressing && progress.CompletionPercentage >= _progressThreshold)
            {
                return true;
            }

            // Reschedule if subtasks are being completed
            if (progress.TotalSubtasks > 0 && progress.CompletedSubtasks > 0)
            {
                var completionRate = (double)progress.CompletedSubtasks / progress.TotalSubtasks;
                return completionRate >= _progressThreshold;
            }

            // Don't reschedule if no progress is being made
            return false;
        }

        public bool ShouldAbort(AgentContext context, ActorState state)
        {
            return state == ActorState.Faulted || state == ActorState.Stopped;
        }
    }

    /// <summary>
    /// Budget-aware timeout policy that manages timeout budgets across parent-child agent hierarchies.
    /// Ensures that child agents don't exceed the timeout budget allocated by their parents.
    /// </summary>
    public class BudgetAwareTimeoutPolicy : ITimeoutPolicy
    {
        private readonly TimeSpan _defaultTimeout;
        private readonly double _budgetReserveRatio;
        private readonly double _progressThreshold;

        public BudgetAwareTimeoutPolicy(
            TimeSpan defaultTimeout,
            double budgetReserveRatio = 0.2,
            double progressThreshold = 0.15)
        {
            _defaultTimeout = defaultTimeout;
            _budgetReserveRatio = Math.Max(0.0, Math.Min(0.5, budgetReserveRatio));
            _progressThreshold = Math.Max(0.0, Math.Min(1.0, progressThreshold));
        }

        public TimeSpan GetTimeout(AgentContext context)
        {
            // If no budget is provided, use default timeout
            if (!context.TimeoutBudget.HasValue)
            {
                return _defaultTimeout;
            }

            var budget = context.TimeoutBudget.Value;
            var elapsed = DateTimeOffset.UtcNow - context.OperationStartTime;
            var remaining = budget - elapsed;

            // If we've already exceeded the budget, give a small grace period
            if (remaining <= TimeSpan.Zero)
            {
                return TimeSpan.FromSeconds(30);
            }

            // Reserve some budget for cleanup and parent coordination
            var reserveAmount = TimeSpan.FromMilliseconds(budget.TotalMilliseconds * _budgetReserveRatio);
            var availableTimeout = remaining - reserveAmount;

            // Ensure we have at least a minimum timeout
            if (availableTimeout < TimeSpan.FromSeconds(30))
            {
                availableTimeout = TimeSpan.FromSeconds(30);
            }

            return availableTimeout;
        }

        public bool ShouldReschedule(AgentContext context, AgentProgress progress)
        {
            // Only reschedule if we have budget remaining and are making progress
            if (!context.TimeoutBudget.HasValue)
            {
                return progress.IsActivelyProgressing;
            }

            var budget = context.TimeoutBudget.Value;
            var elapsed = DateTimeOffset.UtcNow - context.OperationStartTime;
            var budgetUsageRatio = elapsed.TotalMilliseconds / budget.TotalMilliseconds;

            // Don't reschedule if we've used most of our budget
            if (budgetUsageRatio > 0.8)
            {
                return false;
            }

            // Reschedule if making good progress
            return progress.IsActivelyProgressing && progress.CompletionPercentage >= _progressThreshold;
        }

        public bool ShouldAbort(AgentContext context, ActorState state)
        {
            if (state == ActorState.Faulted || state == ActorState.Stopped)
            {
                return true;
            }

            // Abort if we've significantly exceeded the budget
            if (context.TimeoutBudget.HasValue)
            {
                var budget = context.TimeoutBudget.Value;
                var elapsed = DateTimeOffset.UtcNow - context.OperationStartTime;
                var budgetUsageRatio = elapsed.TotalMilliseconds / budget.TotalMilliseconds;

                // Abort if we've used more than 150% of our budget
                if (budgetUsageRatio > 1.5)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Agent-type-specific timeout policy that applies different timeout rules based on agent type.
    /// Useful for systems with different types of agents that have different timeout requirements.
    /// </summary>
    public class AgentTypeTimeoutPolicy : ITimeoutPolicy
    {
        private readonly TimeSpan _defaultTimeout;
        private readonly Dictionary<string, TimeSpan> _agentTypeTimeouts;
        private readonly Dictionary<string, bool> _agentTypeAllowReschedule;

        public AgentTypeTimeoutPolicy(TimeSpan defaultTimeout)
        {
            _defaultTimeout = defaultTimeout;
            _agentTypeTimeouts = new Dictionary<string, TimeSpan>();
            _agentTypeAllowReschedule = new Dictionary<string, bool>();
        }

        /// <summary>
        /// Configures timeout settings for a specific agent type.
        /// </summary>
        /// <param name="agentType">The agent type name</param>
        /// <param name="timeout">Timeout duration for this agent type</param>
        /// <param name="allowReschedule">Whether this agent type can have its timeout rescheduled</param>
        /// <returns>This instance for method chaining</returns>
        public AgentTypeTimeoutPolicy ConfigureAgentType(string agentType, TimeSpan timeout, bool allowReschedule = true)
        {
            _agentTypeTimeouts[agentType] = timeout;
            _agentTypeAllowReschedule[agentType] = allowReschedule;
            return this;
        }

        public TimeSpan GetTimeout(AgentContext context)
        {
            if (_agentTypeTimeouts.TryGetValue(context.AgentType, out var timeout))
            {
                return timeout;
            }

            return _defaultTimeout;
        }

        public bool ShouldReschedule(AgentContext context, AgentProgress progress)
        {
            // Check if this agent type allows rescheduling
            if (_agentTypeAllowReschedule.TryGetValue(context.AgentType, out var allowReschedule))
            {
                return allowReschedule && progress.IsActivelyProgressing;
            }

            // Default behavior: allow rescheduling if making progress
            return progress.IsActivelyProgressing;
        }

        public bool ShouldAbort(AgentContext context, ActorState state)
        {
            return state == ActorState.Faulted || state == ActorState.Stopped;
        }
    }

    /// <summary>
    /// Composite timeout policy that combines multiple policies using different strategies.
    /// Allows for complex timeout logic by combining simpler policies.
    /// </summary>
    public class CompositeTimeoutPolicy : ITimeoutPolicy
    {
        private readonly ITimeoutPolicy[] _policies;
        private readonly CompositeStrategy _strategy;

        public enum CompositeStrategy
        {
            /// <summary>
            /// Use the minimum timeout from all policies.
            /// </summary>
            MinimumTimeout,
            /// <summary>
            /// Use the maximum timeout from all policies.
            /// </summary>
            MaximumTimeout,
            /// <summary>
            /// Use the first policy's timeout.
            /// </summary>
            FirstPolicy,
            /// <summary>
            /// Use the average timeout from all policies.
            /// </summary>
            AverageTimeout
        }

        public CompositeTimeoutPolicy(CompositeStrategy strategy, params ITimeoutPolicy[] policies)
        {
            _strategy = strategy;
            _policies = policies ?? throw new ArgumentNullException(nameof(policies));
            
            if (_policies.Length == 0)
            {
                throw new ArgumentException("At least one policy must be provided", nameof(policies));
            }
        }

        public TimeSpan GetTimeout(AgentContext context)
        {
            var timeouts = _policies.Select(p => p.GetTimeout(context)).ToArray();

            return _strategy switch
            {
                CompositeStrategy.MinimumTimeout => timeouts.Min(),
                CompositeStrategy.MaximumTimeout => timeouts.Max(),
                CompositeStrategy.FirstPolicy => timeouts[0],
                CompositeStrategy.AverageTimeout => TimeSpan.FromMilliseconds(timeouts.Average(t => t.TotalMilliseconds)),
                _ => timeouts[0]
            };
        }

        public bool ShouldReschedule(AgentContext context, AgentProgress progress)
        {
            // All policies must agree to reschedule
            return _policies.All(p => p.ShouldReschedule(context, progress));
        }

        public bool ShouldAbort(AgentContext context, ActorState state)
        {
            // Any policy can trigger an abort
            return _policies.Any(p => p.ShouldAbort(context, state));
        }
    }
} 