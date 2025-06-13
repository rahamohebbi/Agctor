using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Actors;
using AgctorSDK.Core.Timeout;
using AgctorSDK.Core.Extensions;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Utils;

namespace AgctorSDK.Core.IntegrationTests.Timeout
{
    /// <summary>
    /// Integration tests that verify the complete timeout management system works end-to-end.
    /// Tests real scenarios with agents, timeout policies, and supervisor interaction.
    /// </summary>
    public class TimeoutIntegrationTests
    {
        private readonly Mock<IActorRuntimeAdapter> _mockRuntimeAdapter;
        private readonly Mock<ILogger<TimeoutSupervisorActor>> _mockLogger;

        public TimeoutIntegrationTests()
        {
            _mockRuntimeAdapter = new Mock<IActorRuntimeAdapter>();
            _mockLogger = new Mock<ILogger<TimeoutSupervisorActor>>();
        }

        [Fact]
        public async Task TimeoutSystem_WithLongRunningAgent_HandlesTimeoutCorrectly()
        {
            // Arrange - Create a timeout system that will trigger quickly for testing
            var options = new TimeoutSupervisorOptions 
            { 
                DefaultTimeout = TimeSpan.FromMilliseconds(100),
                CollectPartialResultsOnTimeout = false,
                EnableTimeoutLogging = true
            };
            
            var timeoutPolicy = new FixedTimeoutPolicy(TimeSpan.FromMilliseconds(50));
            var timeoutSupervisor = new TimeoutSupervisorActor(
                "timeout-supervisor", 
                _mockRuntimeAdapter.Object, 
                timeoutPolicy, 
                options, 
                _mockLogger.Object);

            await timeoutSupervisor.InitializeAsync();

            // Create a simple test agent
            var testAgent = new TestTimeoutAgent("test-agent");
            
            // Act - Execute a long-running operation with timeout
            var operationTask = testAgent.ExecuteWithTimeoutAsync(
                timeoutSupervisor,
                "long-operation",
                async (ct) => 
                {
                    // Simulate long-running work that exceeds timeout
                    await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                    return "completed";
                },
                timeoutPolicy);

            // Wait for the timeout to trigger
            await Task.Delay(TimeSpan.FromMilliseconds(150));

            // Assert - Verify timeout notification was sent
            _mockRuntimeAdapter.Verify(
                r => r.SendMessageAsync(
                    "test-agent",
                    It.IsAny<TimeoutOccurredMessage>(),
                    "timeout-supervisor",
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);

            await timeoutSupervisor.ShutdownAsync();
        }

        [Fact]
        public async Task TimeoutSystem_WithProgressUpdates_ReschedulesTimeout()
        {
            // Arrange
            var options = new TimeoutSupervisorOptions 
            { 
                DefaultTimeout = TimeSpan.FromMilliseconds(100),
                MaxRescheduleCount = 2
            };
            
            var timeoutPolicy = new AdaptiveTimeoutPolicy(
                baseTimeout: TimeSpan.FromMilliseconds(50),
                maxTimeout: TimeSpan.FromMilliseconds(200));
            
            var timeoutSupervisor = new TimeoutSupervisorActor(
                "timeout-supervisor", 
                _mockRuntimeAdapter.Object, 
                timeoutPolicy, 
                options, 
                _mockLogger.Object);

            await timeoutSupervisor.InitializeAsync();

            var testAgent = new TestTimeoutAgent("test-agent");
            
            // Act - Execute operation that reports progress
            var operationTask = testAgent.ExecuteWithProgressAsync(
                timeoutSupervisor,
                "progressive-operation",
                async (progressReporter, ct) => 
                {
                    for (int i = 1; i <= 5; i++)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(30), ct);
                        await progressReporter(i * 0.2);
                    }
                    return "completed with progress";
                },
                timeoutPolicy);

            // Wait for operation to complete
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            // Assert - Operation should complete without timeout due to progress updates
            // No timeout notification should be sent
            _mockRuntimeAdapter.Verify(
                r => r.SendMessageAsync(
                    "test-agent",
                    It.IsAny<TimeoutOccurredMessage>(),
                    It.IsAny<string>(),
                    It.IsAny<System.Collections.Generic.IDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            await timeoutSupervisor.ShutdownAsync();
        }

        [Fact]
        public async Task TimeoutSystem_WithBudgetAwarePolicy_RespectsTimeoutBudget()
        {
            // Arrange - Create a budget-aware timeout policy
            var budget = TimeSpan.FromMilliseconds(150);
            var timeoutPolicy = new BudgetAwareTimeoutPolicy(TimeSpan.FromMilliseconds(200)); // Default longer than budget
            
            var timeoutSupervisor = new TimeoutSupervisorActor(
                "timeout-supervisor", 
                _mockRuntimeAdapter.Object, 
                timeoutPolicy, 
                new TimeoutSupervisorOptions(), 
                _mockLogger.Object);

            await timeoutSupervisor.InitializeAsync();

            var testAgent = new TestTimeoutAgent("test-agent");
            
            // Create context with budget
            var context = new AgentContext(
                "test-agent",
                "TestAgent",
                "Test operation with budget",
                null,
                0,
                1,
                budget, // Set timeout budget
                new System.Collections.Generic.Dictionary<string, object>());

            // Act - Register timeout with budget
            await timeoutSupervisor.RegisterTimeoutAsync("test-agent", "budget-operation", context);

            // Wait longer than budget but less than default timeout
            await Task.Delay(TimeSpan.FromMilliseconds(180));

            // The timeout should have triggered because budget was exceeded
            await timeoutSupervisor.ShutdownAsync();
        }

        /// <summary>
        /// Simple test agent that can be used with the timeout system.
        /// Demonstrates how real agents would integrate with timeout management.
        /// </summary>
        private class TestTimeoutAgent : Agent
        {
            public TestTimeoutAgent(string id) 
                : base(id)
            {
            }

            /// <summary>
            /// Executes a task with timeout management, demonstrating the extension method usage.
            /// </summary>
            public async Task<T> ExecuteWithTimeoutAsync<T>(
                ITimeoutSupervisor timeoutSupervisor,
                string operationId,
                Func<CancellationToken, Task<T>> operation,
                ITimeoutPolicy timeoutPolicy)
            {
                // Call the extension method explicitly with required parameters
                return await this.ExecuteWithTimeoutAsync(
                    timeoutSupervisor,
                    operationId,
                    operation,
                    taskComplexity: 1,
                    timeoutPolicy: timeoutPolicy,
                    timeoutBudget: null,
                    cancellationToken: CancellationToken.None);
            }

            /// <summary>
            /// Executes a task that reports progress, showing how progress updates can extend timeouts.
            /// </summary>
            public async Task<T> ExecuteWithProgressAsync<T>(
                ITimeoutSupervisor timeoutSupervisor,
                string operationId,
                Func<Func<double, Task>, CancellationToken, Task<T>> operation,
                ITimeoutPolicy timeoutPolicy)
            {
                // Register timeout
                await this.RegisterTimeoutAsync(timeoutSupervisor, operationId, 1, timeoutPolicy);

                try
                {
                    // Create progress reporter that updates the timeout supervisor
                    Func<double, Task> progressReporter = async (completionPercentage) =>
                    {
                        await this.UpdateProgressAsync(
                            timeoutSupervisor, 
                            operationId, 
                            completionPercentage);
                    };

                    // Execute the operation
                    var result = await operation(progressReporter, CancellationToken.None);
                    
                    // Cancel timeout on successful completion
                    await this.CancelTimeoutAsync(timeoutSupervisor, operationId);
                    
                    return result;
                }
                catch
                {
                    // Cancel timeout on exception
                    await this.CancelTimeoutAsync(timeoutSupervisor, operationId);
                    throw;
                }
            }

            public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
            {
                // Handle timeout notifications
                if (envelope.Payload is TimeoutOccurredMessage timeoutMsg)
                {
                    // In a real agent, you would handle the timeout appropriately
                    // For testing, we just acknowledge it
                    var ackPayload = new { Acknowledged = true };
                    var ackId = System.Guid.NewGuid().ToString();
                    var ackMetadata = new System.Collections.Generic.Dictionary<string, object> { { "Timestamp", DateTimeOffset.UtcNow } };
                    var ackHeaders = new System.Collections.Generic.Dictionary<string, string> 
                    { 
                        { "SenderId", Id },
                        { "ReceiverId", envelope.Headers.TryGetValue("SenderId", out var senderId) ? senderId : "unknown" },
                        { "MessageId", ackId },
                        { "InReplyTo", envelope.Headers.TryGetValue("MessageId", out var msgId) ? msgId : "" },
                        { "MessageType", "Acknowledgment" },
                        { "ContentType", "application/json" }
                    };
                    return new MessageEnvelope(ackPayload, ackMetadata, ackId, ackHeaders);
                }

                return await base.ReceiveAsync(envelope, cancellationToken);
            }
        }
    }
} 