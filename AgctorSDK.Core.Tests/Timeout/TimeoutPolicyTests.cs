using System;
using System.Collections.Generic;
using Xunit;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Timeout;

namespace AgctorSDK.Core.Tests.Timeout
{
    /// <summary>
    /// Unit tests for timeout policy implementations.
    /// Tests various timeout policies to ensure they behave correctly under different scenarios.
    /// </summary>
    public class TimeoutPolicyTests
    {
        [Fact]
        public void FixedTimeoutPolicy_ReturnsFixedTimeout()
        {
            // Arrange
            var timeout = TimeSpan.FromMinutes(5);
            var policy = new FixedTimeoutPolicy(timeout);
            var context = CreateTestContext();

            // Act
            var result = policy.GetTimeout(context);

            // Assert
            Assert.Equal(timeout, result);
        }

        [Fact]
        public void FixedTimeoutPolicy_WithRescheduleDisabled_NeverReschedules()
        {
            // Arrange
            var policy = new FixedTimeoutPolicy(TimeSpan.FromMinutes(5), allowReschedule: false);
            var context = CreateTestContext();
            var progress = CreateProgressWithActivity();

            // Act
            var shouldReschedule = policy.ShouldReschedule(context, progress);

            // Assert
            Assert.False(shouldReschedule);
        }

        [Fact]
        public void FixedTimeoutPolicy_WithRescheduleEnabled_ReschedulesWhenProgressing()
        {
            // Arrange
            var policy = new FixedTimeoutPolicy(TimeSpan.FromMinutes(5), allowReschedule: true);
            var context = CreateTestContext();
            var activeProgress = CreateProgressWithActivity();
            var inactiveProgress = CreateProgressWithoutActivity();

            // Act
            var shouldRescheduleActive = policy.ShouldReschedule(context, activeProgress);
            var shouldRescheduleInactive = policy.ShouldReschedule(context, inactiveProgress);

            // Assert
            Assert.True(shouldRescheduleActive);
            Assert.False(shouldRescheduleInactive);
        }

        [Fact]
        public void AdaptiveTimeoutPolicy_AdjustsForComplexity()
        {
            // Arrange
            var baseTimeout = TimeSpan.FromMinutes(5);
            var policy = new AdaptiveTimeoutPolicy(baseTimeout, complexityMultiplier: 2.0);
            
            var simpleContext = CreateTestContext(taskComplexity: 1);
            var complexContext = CreateTestContext(taskComplexity: 3);

            // Act
            var simpleTimeout = policy.GetTimeout(simpleContext);
            var complexTimeout = policy.GetTimeout(complexContext);

            // Assert
            Assert.Equal(baseTimeout, simpleTimeout);
            Assert.True(complexTimeout > simpleTimeout);
        }

        [Fact]
        public void AdaptiveTimeoutPolicy_AdjustsForChildAgents()
        {
            // Arrange
            var baseTimeout = TimeSpan.FromMinutes(5);
            var policy = new AdaptiveTimeoutPolicy(baseTimeout, childAgentMultiplier: 1.5);
            
            var noChildContext = CreateTestContext(childAgentCount: 0);
            var withChildContext = CreateTestContext(childAgentCount: 2);

            // Act
            var noChildTimeout = policy.GetTimeout(noChildContext);
            var withChildTimeout = policy.GetTimeout(withChildContext);

            // Assert
            Assert.True(withChildTimeout > noChildTimeout);
        }

        [Fact]
        public void AdaptiveTimeoutPolicy_RespectsMaxTimeout()
        {
            // Arrange
            var baseTimeout = TimeSpan.FromMinutes(1);
            var maxTimeout = TimeSpan.FromMinutes(2);
            var policy = new AdaptiveTimeoutPolicy(baseTimeout, complexityMultiplier: 10.0, maxTimeout: maxTimeout);
            
            var veryComplexContext = CreateTestContext(taskComplexity: 10);

            // Act
            var timeout = policy.GetTimeout(veryComplexContext);

            // Assert
            Assert.Equal(maxTimeout, timeout);
        }

        [Fact]
        public void AdaptiveTimeoutPolicy_ReschedulesWithProgress()
        {
            // Arrange
            var policy = new AdaptiveTimeoutPolicy(TimeSpan.FromMinutes(5), progressThreshold: 0.2);
            var context = CreateTestContext();
            
            var goodProgress = new AgentProgress(0.3, isActivelyProgressing: true);
            var poorProgress = new AgentProgress(0.1, isActivelyProgressing: true);
            var noProgress = new AgentProgress(0.0, isActivelyProgressing: false);

            // Act
            var shouldRescheduleGood = policy.ShouldReschedule(context, goodProgress);
            var shouldReschedulePoor = policy.ShouldReschedule(context, poorProgress);
            var shouldRescheduleNone = policy.ShouldReschedule(context, noProgress);

            // Assert
            Assert.True(shouldRescheduleGood);
            Assert.False(shouldReschedulePoor);
            Assert.False(shouldRescheduleNone);
        }

        [Fact]
        public void BudgetAwareTimeoutPolicy_UsesDefaultWhenNoBudget()
        {
            // Arrange
            var defaultTimeout = TimeSpan.FromMinutes(10);
            var policy = new BudgetAwareTimeoutPolicy(defaultTimeout);
            var context = CreateTestContext(timeoutBudget: null);

            // Act
            var timeout = policy.GetTimeout(context);

            // Assert
            Assert.Equal(defaultTimeout, timeout);
        }

        [Fact]
        public void BudgetAwareTimeoutPolicy_RespectsRemainingBudget()
        {
            // Arrange
            var defaultTimeout = TimeSpan.FromMinutes(10);
            var policy = new BudgetAwareTimeoutPolicy(defaultTimeout, budgetReserveRatio: 0.2);
            
            var totalBudget = TimeSpan.FromMinutes(5);
            var context = CreateTestContext(timeoutBudget: totalBudget);

            // Act
            var timeout = policy.GetTimeout(context);

            // Assert
            // Should be close to the total budget minus reserve (20% of 5 min = 1 min) = 4 min
            // Since the operation just started, remaining budget should be close to total
            var expectedTimeout = TimeSpan.FromMinutes(4);
            Assert.True(timeout.TotalMinutes >= 3.5 && timeout.TotalMinutes <= 4.5);
        }

        [Fact]
        public void BudgetAwareTimeoutPolicy_AbortsWhenBudgetExceeded()
        {
            // Arrange
            var policy = new BudgetAwareTimeoutPolicy(TimeSpan.FromMinutes(10));
            var budget = TimeSpan.FromSeconds(1); // Very short budget to test exceeded condition
            var context = CreateTestContext(timeoutBudget: budget);
            
            // Wait a bit to exceed the budget
            System.Threading.Thread.Sleep(1500); // 1.5 seconds

            // Act
            var shouldAbort = policy.ShouldAbort(context, ActorState.Active);

            // Assert
            Assert.True(shouldAbort);
        }

        [Fact]
        public void AgentTypeTimeoutPolicy_UsesAgentSpecificTimeout()
        {
            // Arrange
            var defaultTimeout = TimeSpan.FromMinutes(5);
            var llmTimeout = TimeSpan.FromMinutes(10);
            var codeTimeout = TimeSpan.FromMinutes(2);
            
            var policy = new AgentTypeTimeoutPolicy(defaultTimeout)
                .ConfigureAgentType("LLMAgent", llmTimeout)
                .ConfigureAgentType("CodeExecutorAgent", codeTimeout);

            var llmContext = CreateTestContext(agentType: "LLMAgent");
            var codeContext = CreateTestContext(agentType: "CodeExecutorAgent");
            var unknownContext = CreateTestContext(agentType: "UnknownAgent");

            // Act
            var llmResult = policy.GetTimeout(llmContext);
            var codeResult = policy.GetTimeout(codeContext);
            var unknownResult = policy.GetTimeout(unknownContext);

            // Assert
            Assert.Equal(llmTimeout, llmResult);
            Assert.Equal(codeTimeout, codeResult);
            Assert.Equal(defaultTimeout, unknownResult);
        }

        [Fact]
        public void AgentTypeTimeoutPolicy_RespectsRescheduleSettings()
        {
            // Arrange
            var policy = new AgentTypeTimeoutPolicy(TimeSpan.FromMinutes(5))
                .ConfigureAgentType("AllowReschedule", TimeSpan.FromMinutes(5), allowReschedule: true)
                .ConfigureAgentType("NoReschedule", TimeSpan.FromMinutes(5), allowReschedule: false);

            var allowContext = CreateTestContext(agentType: "AllowReschedule");
            var noAllowContext = CreateTestContext(agentType: "NoReschedule");
            var unknownContext = CreateTestContext(agentType: "Unknown");
            
            var progress = CreateProgressWithActivity();

            // Act
            var allowResult = policy.ShouldReschedule(allowContext, progress);
            var noAllowResult = policy.ShouldReschedule(noAllowContext, progress);
            var unknownResult = policy.ShouldReschedule(unknownContext, progress);

            // Assert
            Assert.True(allowResult);
            Assert.False(noAllowResult);
            Assert.True(unknownResult); // Default allows rescheduling
        }

        [Theory]
        [InlineData(CompositeTimeoutPolicy.CompositeStrategy.MinimumTimeout)]
        [InlineData(CompositeTimeoutPolicy.CompositeStrategy.MaximumTimeout)]
        [InlineData(CompositeTimeoutPolicy.CompositeStrategy.FirstPolicy)]
        [InlineData(CompositeTimeoutPolicy.CompositeStrategy.AverageTimeout)]
        public void CompositeTimeoutPolicy_AppliesCorrectStrategy(CompositeTimeoutPolicy.CompositeStrategy strategy)
        {
            // Arrange
            var policy1 = new FixedTimeoutPolicy(TimeSpan.FromMinutes(2));
            var policy2 = new FixedTimeoutPolicy(TimeSpan.FromMinutes(6));
            var policy3 = new FixedTimeoutPolicy(TimeSpan.FromMinutes(4));
            
            var composite = new CompositeTimeoutPolicy(strategy, policy1, policy2, policy3);
            var context = CreateTestContext();

            // Act
            var result = composite.GetTimeout(context);

            // Assert
            switch (strategy)
            {
                case CompositeTimeoutPolicy.CompositeStrategy.MinimumTimeout:
                    Assert.Equal(TimeSpan.FromMinutes(2), result);
                    break;
                case CompositeTimeoutPolicy.CompositeStrategy.MaximumTimeout:
                    Assert.Equal(TimeSpan.FromMinutes(6), result);
                    break;
                case CompositeTimeoutPolicy.CompositeStrategy.FirstPolicy:
                    Assert.Equal(TimeSpan.FromMinutes(2), result);
                    break;
                case CompositeTimeoutPolicy.CompositeStrategy.AverageTimeout:
                    Assert.Equal(TimeSpan.FromMinutes(4), result);
                    break;
            }
        }

        [Fact]
        public void CompositeTimeoutPolicy_RequiresAllPoliciesToAgreeOnReschedule()
        {
            // Arrange
            var allowPolicy = new FixedTimeoutPolicy(TimeSpan.FromMinutes(5), allowReschedule: true);
            var denyPolicy = new FixedTimeoutPolicy(TimeSpan.FromMinutes(5), allowReschedule: false);
            
            var composite = new CompositeTimeoutPolicy(
                CompositeTimeoutPolicy.CompositeStrategy.FirstPolicy, 
                allowPolicy, 
                denyPolicy);
            
            var context = CreateTestContext();
            var progress = CreateProgressWithActivity();

            // Act
            var shouldReschedule = composite.ShouldReschedule(context, progress);

            // Assert
            Assert.False(shouldReschedule); // All policies must agree
        }

        [Fact]
        public void CompositeTimeoutPolicy_AnyPolicyCanTriggerAbort()
        {
            // Arrange
            var normalPolicy = new FixedTimeoutPolicy(TimeSpan.FromMinutes(5));
            var strictPolicy = new TestAbortPolicy(); // Always aborts
            
            var composite = new CompositeTimeoutPolicy(
                CompositeTimeoutPolicy.CompositeStrategy.FirstPolicy,
                normalPolicy,
                strictPolicy);
            
            var context = CreateTestContext();

            // Act
            var shouldAbort = composite.ShouldAbort(context, ActorState.Active);

            // Assert
            Assert.True(shouldAbort);
        }

        private static AgentContext CreateTestContext(
            string agentId = "test-agent",
            string agentType = "TestAgent",
            string? currentPrompt = "Test prompt",
            string? parentAgentId = null,
            int childAgentCount = 0,
            int taskComplexity = 1,
            TimeSpan? timeoutBudget = null,
            DateTimeOffset? operationStartTime = null)
        {
            var context = new AgentContext(
                agentId,
                agentType,
                currentPrompt,
                parentAgentId,
                childAgentCount,
                taskComplexity,
                timeoutBudget,
                new Dictionary<string, object>());
            
            // For testing purposes, we can't easily override the operation start time
            // since it's set in the constructor. For budget tests, we'll work with the actual time.
            return context;
        }

        private static AgentProgress CreateProgressWithActivity()
        {
            return new AgentProgress(0.5, currentActivity: "Working", isActivelyProgressing: true);
        }

        private static AgentProgress CreateProgressWithoutActivity()
        {
            return new AgentProgress(0.1, currentActivity: "Idle", isActivelyProgressing: false);
        }

        /// <summary>
        /// Test policy that always aborts - used for testing composite policies.
        /// </summary>
        private class TestAbortPolicy : ITimeoutPolicy
        {
            public TimeSpan GetTimeout(AgentContext context) => TimeSpan.FromMinutes(5);
            public bool ShouldReschedule(AgentContext context, AgentProgress progress) => false;
            public bool ShouldAbort(AgentContext context, ActorState state) => true;
        }
    }
} 