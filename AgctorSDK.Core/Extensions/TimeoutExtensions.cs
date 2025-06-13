using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.Extensions
{
    /// <summary>
    /// Extension methods to simplify timeout management integration for agents and actors.
    /// Provides convenient methods for registering timeouts, updating progress, and handling timeout notifications.
    /// </summary>
    public static class TimeoutExtensions
    {
        /// <summary>
        /// Registers a timeout for an agent operation with the timeout supervisor.
        /// Automatically creates the agent context from the agent's current state.
        /// </summary>
        /// <param name="agent">The agent to register timeout for</param>
        /// <param name="timeoutSupervisor">The timeout supervisor to register with</param>
        /// <param name="operationId">Unique identifier for the operation</param>
        /// <param name="taskComplexity">Estimated complexity of the task (default: 1)</param>
        /// <param name="timeoutPolicy">Optional specific timeout policy for this operation</param>
        /// <param name="timeoutBudget">Optional timeout budget from parent agent</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the registration operation</returns>
        public static async Task RegisterTimeoutAsync(
            this IAgent agent,
            ITimeoutSupervisor timeoutSupervisor,
            string operationId,
            int taskComplexity = 1,
            ITimeoutPolicy? timeoutPolicy = null,
            TimeSpan? timeoutBudget = null,
            CancellationToken cancellationToken = default)
        {
            var context = new AgentContext(
                agent.Id,
                agent.ActorType,
                agent.CurrentPrompt,
                agent.ParentAgentId,
                agent.ChildAgentIds.Count,
                taskComplexity,
                timeoutBudget);

            await timeoutSupervisor.RegisterTimeoutAsync(agent.Id, operationId, context, cancellationToken);
        }

        /// <summary>
        /// Cancels timeout monitoring for a completed operation.
        /// </summary>
        /// <param name="agent">The agent that completed the operation</param>
        /// <param name="timeoutSupervisor">The timeout supervisor</param>
        /// <param name="operationId">ID of the completed operation</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the cancellation operation</returns>
        public static async Task CancelTimeoutAsync(
            this IAgent agent,
            ITimeoutSupervisor timeoutSupervisor,
            string operationId,
            CancellationToken cancellationToken = default)
        {
            await timeoutSupervisor.CancelTimeoutAsync(agent.Id, operationId, cancellationToken);
        }

        /// <summary>
        /// Updates progress for an ongoing operation.
        /// </summary>
        /// <param name="agent">The agent reporting progress</param>
        /// <param name="timeoutSupervisor">The timeout supervisor</param>
        /// <param name="operationId">ID of the operation</param>
        /// <param name="progress">Current progress information</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the progress update operation</returns>
        public static async Task UpdateProgressAsync(
            this IAgent agent,
            ITimeoutSupervisor timeoutSupervisor,
            string operationId,
            AgentProgress progress,
            CancellationToken cancellationToken = default)
        {
            await timeoutSupervisor.UpdateProgressAsync(agent.Id, operationId, progress, cancellationToken);
        }

        /// <summary>
        /// Creates a simple progress update indicating completion percentage.
        /// </summary>
        /// <param name="agent">The agent reporting progress</param>
        /// <param name="timeoutSupervisor">The timeout supervisor</param>
        /// <param name="operationId">ID of the operation</param>
        /// <param name="completionPercentage">Completion percentage (0.0 to 1.0)</param>
        /// <param name="currentActivity">Optional description of current activity</param>
        /// <param name="partialResults">Optional partial results</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the progress update operation</returns>
        public static async Task UpdateProgressAsync(
            this IAgent agent,
            ITimeoutSupervisor timeoutSupervisor,
            string operationId,
            double completionPercentage,
            string? currentActivity = null,
            object? partialResults = null,
            CancellationToken cancellationToken = default)
        {
            var progress = new AgentProgress(
                completionPercentage,
                currentActivity: currentActivity,
                partialResults: partialResults);

            await agent.UpdateProgressAsync(timeoutSupervisor, operationId, progress, cancellationToken);
        }

        /// <summary>
        /// Creates a progress update based on subtask completion.
        /// </summary>
        /// <param name="agent">The agent reporting progress</param>
        /// <param name="timeoutSupervisor">The timeout supervisor</param>
        /// <param name="operationId">ID of the operation</param>
        /// <param name="completedSubtasks">Number of completed subtasks</param>
        /// <param name="totalSubtasks">Total number of subtasks</param>
        /// <param name="currentActivity">Optional description of current activity</param>
        /// <param name="partialResults">Optional partial results</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the progress update operation</returns>
        public static async Task UpdateSubtaskProgressAsync(
            this IAgent agent,
            ITimeoutSupervisor timeoutSupervisor,
            string operationId,
            int completedSubtasks,
            int totalSubtasks,
            string? currentActivity = null,
            object? partialResults = null,
            CancellationToken cancellationToken = default)
        {
            var completionPercentage = totalSubtasks > 0 ? (double)completedSubtasks / totalSubtasks : 0.0;
            var progress = new AgentProgress(
                completionPercentage,
                completedSubtasks,
                totalSubtasks,
                currentActivity,
                partialResults: partialResults);

            await agent.UpdateProgressAsync(timeoutSupervisor, operationId, progress, cancellationToken);
        }

        /// <summary>
        /// Executes an operation with automatic timeout management.
        /// Registers the timeout, executes the operation, and cancels the timeout upon completion.
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="agent">The agent executing the operation</param>
        /// <param name="timeoutSupervisor">The timeout supervisor</param>
        /// <param name="operationId">Unique identifier for the operation</param>
        /// <param name="operation">The operation to execute</param>
        /// <param name="taskComplexity">Estimated complexity of the task</param>
        /// <param name="timeoutPolicy">Optional timeout policy</param>
        /// <param name="timeoutBudget">Optional timeout budget</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation</returns>
        public static async Task<T> ExecuteWithTimeoutAsync<T>(
            this IAgent agent,
            ITimeoutSupervisor timeoutSupervisor,
            string operationId,
            Func<CancellationToken, Task<T>> operation,
            int taskComplexity = 1,
            ITimeoutPolicy? timeoutPolicy = null,
            TimeSpan? timeoutBudget = null,
            CancellationToken cancellationToken = default)
        {
            // Register timeout
            await agent.RegisterTimeoutAsync(
                timeoutSupervisor,
                operationId,
                taskComplexity,
                timeoutPolicy,
                timeoutBudget,
                cancellationToken);

            try
            {
                // Execute operation
                var result = await operation(cancellationToken);
                
                // Cancel timeout on successful completion
                await agent.CancelTimeoutAsync(timeoutSupervisor, operationId, cancellationToken);
                
                return result;
            }
            catch
            {
                // Cancel timeout on failure (the timeout supervisor will handle the error notification)
                try
                {
                    await agent.CancelTimeoutAsync(timeoutSupervisor, operationId, cancellationToken);
                }
                catch
                {
                    // Ignore cancellation errors during cleanup
                }
                
                throw;
            }
        }

        /// <summary>
        /// Executes an operation with automatic timeout management (void return).
        /// </summary>
        /// <param name="agent">The agent executing the operation</param>
        /// <param name="timeoutSupervisor">The timeout supervisor</param>
        /// <param name="operationId">Unique identifier for the operation</param>
        /// <param name="operation">The operation to execute</param>
        /// <param name="taskComplexity">Estimated complexity of the task</param>
        /// <param name="timeoutPolicy">Optional timeout policy</param>
        /// <param name="timeoutBudget">Optional timeout budget</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the operation</returns>
        public static async Task ExecuteWithTimeoutAsync(
            this IAgent agent,
            ITimeoutSupervisor timeoutSupervisor,
            string operationId,
            Func<CancellationToken, Task> operation,
            int taskComplexity = 1,
            ITimeoutPolicy? timeoutPolicy = null,
            TimeSpan? timeoutBudget = null,
            CancellationToken cancellationToken = default)
        {
            await agent.ExecuteWithTimeoutAsync(
                timeoutSupervisor,
                operationId,
                async ct =>
                {
                    await operation(ct);
                    return true; // Return dummy value for generic method
                },
                taskComplexity,
                timeoutPolicy,
                timeoutBudget,
                cancellationToken);
        }

        /// <summary>
        /// Handles a timeout notification message in an agent.
        /// Provides default timeout handling behavior that can be customized.
        /// </summary>
        /// <param name="agent">The agent handling the timeout</param>
        /// <param name="timeoutMessage">The timeout notification message</param>
        /// <param name="customHandler">Optional custom timeout handler</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the timeout handling</returns>
        public static async Task HandleTimeoutAsync(
            this IAgent agent,
            TimeoutOccurredMessage timeoutMessage,
            Func<TimeoutOccurredMessage, Task>? customHandler = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (customHandler != null)
                {
                    await customHandler(timeoutMessage);
                }
                else
                {
                    // Default timeout handling
                    await DefaultTimeoutHandlingAsync(agent, timeoutMessage, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Log timeout handling errors but don't propagate them
                // This prevents timeout handling from causing additional failures
                System.Diagnostics.Debug.WriteLine($"Error handling timeout for agent {agent.Id}: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculates timeout budget for child agents based on remaining parent budget.
        /// Helps implement proper timeout budget propagation in hierarchical agent systems.
        /// </summary>
        /// <param name="agent">The parent agent</param>
        /// <param name="totalBudget">Total timeout budget available</param>
        /// <param name="startTime">When the parent operation started</param>
        /// <param name="childCount">Number of child agents to budget for</param>
        /// <param name="reserveRatio">Ratio of budget to reserve for parent coordination (default: 0.2)</param>
        /// <returns>Budget to allocate to each child agent</returns>
        public static TimeSpan CalculateChildTimeoutBudget(
            this IAgent agent,
            TimeSpan totalBudget,
            DateTimeOffset startTime,
            int childCount,
            double reserveRatio = 0.2)
        {
            if (childCount <= 0)
            {
                return totalBudget;
            }

            var elapsed = DateTimeOffset.UtcNow - startTime;
            var remaining = totalBudget - elapsed;

            // If no time remaining, give minimal budget
            if (remaining <= TimeSpan.Zero)
            {
                return TimeSpan.FromSeconds(30);
            }

            // Reserve some budget for parent coordination
            var reserveAmount = TimeSpan.FromMilliseconds(remaining.TotalMilliseconds * reserveRatio);
            var availableBudget = remaining - reserveAmount;

            // Divide available budget among children
            var childBudget = TimeSpan.FromMilliseconds(availableBudget.TotalMilliseconds / childCount);

            // Ensure minimum budget per child
            if (childBudget < TimeSpan.FromSeconds(30))
            {
                childBudget = TimeSpan.FromSeconds(30);
            }

            return childBudget;
        }

        private static async Task DefaultTimeoutHandlingAsync(IAgent agent, TimeoutOccurredMessage timeoutMessage, CancellationToken cancellationToken)
        {
            // Default behavior based on timeout action
            switch (timeoutMessage.Result.Action)
            {
                case TimeoutAction.Cancel:
                    // Stop current work and return partial results if available
                    // This would typically involve setting agent status and cleaning up resources
                    break;

                case TimeoutAction.Abort:
                    // Immediately stop all work without cleanup
                    break;

                case TimeoutAction.Escalate:
                    // Forward timeout to parent agent or supervisor
                    // This is handled by the timeout supervisor automatically
                    break;

                case TimeoutAction.Retry:
                    // Could implement retry logic here
                    break;

                case TimeoutAction.Extend:
                    // Timeout was extended, continue working
                    break;
            }

            await Task.CompletedTask;
        }
    }
} 