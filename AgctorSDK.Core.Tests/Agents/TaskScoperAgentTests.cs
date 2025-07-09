using System;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Agents.Agents;
using AgctorSDK.Core.Goals;
using AgctorSDK.Core.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Agents;

[TestClass]
public class TaskScoperAgentTests
{
    private static TaskScoperAgent CreateAgent(out InMemoryGoalStore goalStore, out InMemoryTaskStore taskStore)
    {
        goalStore = new InMemoryGoalStore(filePath: null);
        taskStore = new InMemoryTaskStore(filePath: null);
        return new TaskScoperAgent("scoper-1", goalStore, taskStore);
    }

    // DAG = Directed Acyclic Graph
    [TestMethod]
    public void GenerateTasks_ShouldCreateTasks_WithDependencies()
    {
        var agent = CreateAgent(out var _, out var _);
        var goal = new Goal
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Description = "Task1\nTask2:Task1\nTask3:Task1,Task2"
        };

        var tasks = agent.GenerateTasks(goal);
        tasks.Should().HaveCount(3);
        var t1 = tasks.First(t => t.Title == "Task1");
        var t2 = tasks.First(t => t.Title == "Task2");
        var t3 = tasks.First(t => t.Title == "Task3");
        t1.Dependencies.Should().BeEmpty();
        t2.Dependencies.Should().ContainSingle(d => d == t1.Id);
        t3.Dependencies.Should().BeEquivalentTo(new[] { t1.Id, t2.Id });
    }

    [TestMethod]
    public async Task ProcessGoals_ShouldPersistTasksAndUpdateGoalStatus()
    {
        var agent = CreateAgent(out var goalStore, out var taskStore);
        var goal = new Goal { Title = "Goal", Description = "A\nB" };
        await goalStore.CreateAsync(goal);

        await agent.ProcessGoalsAsync();

        var tasks = (await taskStore.GetByGoalAsync(goal.Id)).ToList();
        tasks.Should().HaveCount(2);
        (await goalStore.GetAsync(goal.Id))!.Status.Should().Be(GoalStatus.InProgress);
    }
} 