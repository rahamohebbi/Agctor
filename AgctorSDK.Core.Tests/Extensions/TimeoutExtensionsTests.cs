using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Extensions;

namespace AgctorSDK.Core.Tests.Extensions
{
    /// <summary>
    /// Unit tests for the TimeoutExtensions helper methods.
    /// Tests the convenience methods that make it easier for agents to integrate with timeout management.
    /// </summary>
    public class TimeoutExtensionsTests
    {
        private readonly Mock<IAgent> _mockAgent;
        private readonly Mock<ITimeoutSupervisor> _mockTimeoutSupervisor;
        private readonly Mock<ITimeoutPolicy> _mockTimeoutPolicy;

        public TimeoutExtensionsTests()
        {
            _mockAgent = new Mock<IAgent>();
            _mockTimeoutSupervisor = new Mock<ITimeoutSupervisor>();
            _mockTimeoutPolicy = new Mock<ITimeoutPolicy>();

            // Setup common agent properties
            _mockAgent.Setup(a => a.Id).Returns("test-agent");
            _mockAgent.Setup(a => a.ActorType).Returns("TestAgent");
            _mockAgent.Setup(a => a.CurrentPrompt).Returns("Test prompt");
            _mockAgent.Setup(a => a.ParentAgentId).Returns((string?)null);
            _mockAgent.Setup(a => a.ChildAgentIds).Returns(new List<string>());
        }

        [Fact]
        public async Task RegisterTimeoutAsync_CreatesContextFromAgent()
        {
            // Arrange
            var operationId = "test-operation";
            var taskComplexity = 3;
            var timeoutBudget = TimeSpan.FromMinutes(10);

            // Act
            await _mockAgent.Object.RegisterTimeoutAsync(
                _mockTimeoutSupervisor.Object,
                operationId,
                taskComplexity,
                _mockTimeoutPolicy.Object,
                timeoutBudget);

            // Assert
            _mockTimeoutSupervisor.Verify(
                ts => ts.RegisterTimeoutAsync(
                    "test-agent",
                    operationId,
                    It.Is<AgentContext>(ctx =>
                        ctx.AgentId == "test-agent" &&
                        ctx.AgentType == "TestAgent" &&
                        ctx.CurrentPrompt == "Test prompt" &&
                        ctx.TaskComplexity == taskComplexity &&
                        ctx.TimeoutBudget == timeoutBudget),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task CancelTimeoutAsync_ForwardsToSupervisor()
        {
            // Arrange
            var operationId = "test-operation";

            // Act
            await _mockAgent.Object.CancelTimeoutAsync(_mockTimeoutSupervisor.Object, operationId);

            // Assert
            _mockTimeoutSupervisor.Verify(
                ts => ts.CancelTimeoutAsync("test-agent", operationId, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdateProgressAsync_WithAgentProgress_ForwardsToSupervisor()
        {
            // Arrange
            var operationId = "test-operation";
            var progress = new AgentProgress(0.5, currentActivity: "Working", isActivelyProgressing: true);

            // Act
            await _mockAgent.Object.UpdateProgressAsync(_mockTimeoutSupervisor.Object, operationId, progress);

            // Assert
            _mockTimeoutSupervisor.Verify(
                ts => ts.UpdateProgressAsync("test-agent", operationId, progress, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdateProgressAsync_WithCompletionPercentage_CreatesProgress()
        {
            // Arrange
            var operationId = "test-operation";
            var completionPercentage = 0.75;
            var currentActivity = "Processing data";
            var partialResults = new { Data = "Some results" };

            // Act
            await _mockAgent.Object.UpdateProgressAsync(
                _mockTimeoutSupervisor.Object,
                operationId,
                completionPercentage,
                currentActivity,
                partialResults);

            // Assert
            _mockTimeoutSupervisor.Verify(
                ts => ts.UpdateProgressAsync(
                    "test-agent",
                    operationId,
                    It.Is<AgentProgress>(p =>
                        p.CompletionPercentage == completionPercentage &&
                        p.CurrentActivity == currentActivity &&
                        p.PartialResults == partialResults),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdateSubtaskProgressAsync_CalculatesCompletionPercentage()
        {
            // Arrange
            var operationId = "test-operation";
            var completedSubtasks = 3;
            var totalSubtasks = 5;
            var currentActivity = "Processing subtasks";
            var partialResults = new { Results = "Partial work" };

            // Act
            await _mockAgent.Object.UpdateSubtaskProgressAsync(
                _mockTimeoutSupervisor.Object,
                operationId,
                completedSubtasks,
                totalSubtasks,
                currentActivity,
                partialResults);

            // Assert
            _mockTimeoutSupervisor.Verify(
                ts => ts.UpdateProgressAsync(
                    "test-agent",
                    operationId,
                    It.Is<AgentProgress>(p =>
                        p.CompletionPercentage == 0.6 && // 3/5 = 0.6
                        p.CompletedSubtasks == completedSubtasks &&
                        p.TotalSubtasks == totalSubtasks &&
                        p.CurrentActivity == currentActivity &&
                        p.PartialResults == partialResults),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdateSubtaskProgressAsync_HandlesZeroSubtasks()
        {
            // Arrange
            var operationId = "test-operation";
            var completedSubtasks = 0;
            var totalSubtasks = 0;

            // Act
            await _mockAgent.Object.UpdateSubtaskProgressAsync(
                _mockTimeoutSupervisor.Object,
                operationId,
                completedSubtasks,
                totalSubtasks);

            // Assert
            _mockTimeoutSupervisor.Verify(
                ts => ts.UpdateProgressAsync(
                    "test-agent",
                    operationId,
                    It.Is<AgentProgress>(p => p.CompletionPercentage == 0.0),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteWithTimeoutAsync_WithReturnValue_RegistersAndCancelsTimeout()
        {
            // Arrange
            var operationId = "test-operation";
            var expectedResult = "Operation completed successfully";
            var operationCallCount = 0;

            Func<CancellationToken, Task<string>> operation = async ct =>
            {
                operationCallCount++;
                await Task.Delay(10, ct); // Simulate work
                return expectedResult;
            };

            // Act
            var result = await _mockAgent.Object.ExecuteWithTimeoutAsync(
                _mockTimeoutSupervisor.Object,
                operationId,
                operation);

            // Assert
            Assert.Equal(expectedResult, result);
            Assert.Equal(1, operationCallCount);

            // Verify timeout registration and cancellation
            _mockTimeoutSupervisor.Verify(
                ts => ts.RegisterTimeoutAsync(
                    "test-agent",
                    operationId,
                    It.IsAny<AgentContext>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _mockTimeoutSupervisor.Verify(
                ts => ts.CancelTimeoutAsync(
                    "test-agent",
                    operationId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteWithTimeoutAsync_VoidReturn_RegistersAndCancelsTimeout()
        {
            // Arrange
            var operationId = "test-operation";
            var operationCallCount = 0;

            Func<CancellationToken, Task> operation = async ct =>
            {
                operationCallCount++;
                await Task.Delay(10, ct); // Simulate work
            };

            // Act
            await _mockAgent.Object.ExecuteWithTimeoutAsync(
                _mockTimeoutSupervisor.Object,
                operationId,
                operation);

            // Assert
            Assert.Equal(1, operationCallCount);

            // Verify timeout registration and cancellation
            _mockTimeoutSupervisor.Verify(
                ts => ts.RegisterTimeoutAsync(
                    "test-agent",
                    operationId,
                    It.IsAny<AgentContext>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _mockTimeoutSupervisor.Verify(
                ts => ts.CancelTimeoutAsync(
                    "test-agent",
                    operationId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteWithTimeoutAsync_OnException_CancelsTimeoutThenRethrows()
        {
            // Arrange
            var operationId = "test-operation";
            var expectedException = new InvalidOperationException("Test error");

            Func<CancellationToken, Task<string>> operation = ct =>
            {
                throw expectedException;
            };

            // Act & Assert
            var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _mockAgent.Object.ExecuteWithTimeoutAsync(
                    _mockTimeoutSupervisor.Object,
                    operationId,
                    operation));

            Assert.Same(expectedException, thrownException);

            // Verify timeout registration and cancellation
            _mockTimeoutSupervisor.Verify(
                ts => ts.RegisterTimeoutAsync(
                    "test-agent",
                    operationId,
                    It.IsAny<AgentContext>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _mockTimeoutSupervisor.Verify(
                ts => ts.CancelTimeoutAsync(
                    "test-agent",
                    operationId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleTimeoutAsync_WithoutCustomHandler_UsesDefaultHandling()
        {
            // Arrange
            var timeoutMessage = new TimeoutOccurredMessage(
                "test-agent",
                "test-operation",
                new TimeoutResult(TimeoutAction.Cancel, "Partial results", "Timeout occurred"),
                new AgentContext("test-agent", "TestAgent"));

            // Act
            await _mockAgent.Object.HandleTimeoutAsync(timeoutMessage);

            // Assert
            // Should not throw - default handling is implemented
        }

        [Fact]
        public async Task HandleTimeoutAsync_WithCustomHandler_CallsCustomHandler()
        {
            // Arrange
            var timeoutMessage = new TimeoutOccurredMessage(
                "test-agent",
                "test-operation",
                new TimeoutResult(TimeoutAction.Cancel, "Partial results", "Timeout occurred"),
                new AgentContext("test-agent", "TestAgent"));

            var customHandlerCalled = false;
            Func<TimeoutOccurredMessage, Task> customHandler = async msg =>
            {
                customHandlerCalled = true;
                Assert.Same(timeoutMessage, msg);
                await Task.CompletedTask;
            };

            // Act
            await _mockAgent.Object.HandleTimeoutAsync(timeoutMessage, customHandler);

            // Assert
            Assert.True(customHandlerCalled);
        }

        [Fact]
        public async Task HandleTimeoutAsync_WithExceptionInCustomHandler_DoesNotPropagate()
        {
            // Arrange
            var timeoutMessage = new TimeoutOccurredMessage(
                "test-agent",
                "test-operation",
                new TimeoutResult(TimeoutAction.Cancel, "Partial results", "Timeout occurred"),
                new AgentContext("test-agent", "TestAgent"));

            Func<TimeoutOccurredMessage, Task> faultyHandler = msg =>
            {
                throw new InvalidOperationException("Handler error");
            };

            // Act & Assert
            // Should not throw - exceptions in timeout handling should be swallowed
            await _mockAgent.Object.HandleTimeoutAsync(timeoutMessage, faultyHandler);
        }

        [Theory]
        [InlineData(1, 10.0, 0.2, 8.0)] // 1 child: (10 - reserve 2) = 8 minutes
        [InlineData(2, 10.0, 0.2, 4.0)] // 2 children: (10 - reserve 2) / 2 = 4 minutes each
        [InlineData(4, 10.0, 0.1, 2.25)] // 4 children: (10 - reserve 1) / 4 = 2.25 minutes each
        [InlineData(0, 10.0, 0.2, 10.0)] // 0 children: returns full budget
        public void CalculateChildTimeoutBudget_ReturnsCorrectBudget(int childCount, double totalMinutes, double reserveRatio, double expectedMinutes)
        {
            // Arrange
            var totalBudget = TimeSpan.FromMinutes(totalMinutes);
            var startTime = DateTimeOffset.UtcNow; // No elapsed time
            var expectedBudget = TimeSpan.FromMinutes(expectedMinutes);

            // Act
            var result = _mockAgent.Object.CalculateChildTimeoutBudget(
                totalBudget,
                startTime,
                childCount,
                reserveRatio);

            // Assert
            Assert.True(Math.Abs(result.TotalMinutes - expectedBudget.TotalMinutes) < 0.1,
                $"Expected {expectedBudget.TotalMinutes} minutes, got {result.TotalMinutes} minutes");
        }

        [Fact]
        public void CalculateChildTimeoutBudget_WithElapsedTime_AdjustsBudget()
        {
            // Arrange
            var totalBudget = TimeSpan.FromMinutes(10);
            var startTime = DateTimeOffset.UtcNow.AddMinutes(-2); // 2 minutes elapsed
            var childCount = 2;
            var reserveRatio = 0.2;

            // Act
            var result = _mockAgent.Object.CalculateChildTimeoutBudget(
                totalBudget,
                startTime,
                childCount,
                reserveRatio);

            // Assert
            // Remaining: 8 minutes, Reserve: 1.6 minutes (20% of 8), Available: 6.4 minutes
            // Per child: 3.2 minutes
            var expectedMinutes = 3.2;
            Assert.True(Math.Abs(result.TotalMinutes - expectedMinutes) < 0.3,
                $"Expected approximately {expectedMinutes} minutes, got {result.TotalMinutes} minutes");
        }

        [Fact]
        public void CalculateChildTimeoutBudget_WhenBudgetExceeded_ReturnsMinimumBudget()
        {
            // Arrange
            var totalBudget = TimeSpan.FromMinutes(5);
            var startTime = DateTimeOffset.UtcNow.AddMinutes(-10); // 10 minutes elapsed, budget exceeded
            var childCount = 2;

            // Act
            var result = _mockAgent.Object.CalculateChildTimeoutBudget(
                totalBudget,
                startTime,
                childCount);

            // Assert
            // Should return minimum budget of 30 seconds
            Assert.Equal(TimeSpan.FromSeconds(30), result);
        }

        [Fact]
        public void CalculateChildTimeoutBudget_WhenResultTooSmall_ReturnsMinimumBudget()
        {
            // Arrange
            var totalBudget = TimeSpan.FromSeconds(60); // 1 minute total
            var startTime = DateTimeOffset.UtcNow;
            var childCount = 10; // Many children
            var reserveRatio = 0.5; // Large reserve

            // Act
            var result = _mockAgent.Object.CalculateChildTimeoutBudget(
                totalBudget,
                startTime,
                childCount,
                reserveRatio);

            // Assert
            // Should return minimum budget of 30 seconds even though calculation would be less
            Assert.Equal(TimeSpan.FromSeconds(30), result);
        }
    }
} 