using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Goals;
using AgctorSDK.Core.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using CoreTaskStatus = AgctorSDK.Core.Tasks.TaskStatus;

namespace AgctorSDK.Core.Tests.Tasks;

[TestClass]
public class TaskFlowEngineTests
{
    private static InMemoryTaskStore CreateStoreWithGraph()
    {
        var store = new InMemoryTaskStore(filePath: null);
        // Clear potential persisted data
        foreach (var t in store.GetAllAsync().Result.ToList())
            store.DeleteAsync(t.Id).Wait();

        // Task A – root
        var a = new ProjectTask { Title = "A" };
        store.CreateAsync(a).Wait();

        // Tasks B and C depend on A
        var b = new ProjectTask { Title = "B", Dependencies = { a.Id } };
        var c = new ProjectTask { Title = "C", Dependencies = { a.Id } };
        store.CreateAsync(b).Wait();
        store.CreateAsync(c).Wait();

        // Task D depends on B and C
        var d = new ProjectTask { Title = "D", Dependencies = { b.Id, c.Id } };
        store.CreateAsync(d).Wait();

        return store;
    }

    [TestMethod]
    public async Task RunOnceAsync_ShouldExecuteReadyTasks_AndRespectDependencies()
    {
        var store = CreateStoreWithGraph();
        var engine = new TaskFlowEngine(store, new SimpleTaskExecutor(), maxParallelism: 2);

        // 1st iteration – only A should complete
        await engine.RunOnceAsync();
        var tasks = (await store.GetAllAsync()).ToList();
        var a = tasks.First(t => t.Title == "A");
        a.Status.Should().Be(CoreTaskStatus.Completed);
        tasks.Where(t => t.Title != "A").Should().OnlyContain(t => t.Status == CoreTaskStatus.Pending);

        // 2nd iteration – B & C should complete (can run in parallel)
        await engine.RunOnceAsync();
        tasks = (await store.GetAllAsync()).ToList();
        tasks.First(t => t.Title == "B").Status.Should().Be(CoreTaskStatus.Completed);
        tasks.First(t => t.Title == "C").Status.Should().Be(CoreTaskStatus.Completed);
        tasks.First(t => t.Title == "D").Status.Should().Be(CoreTaskStatus.Pending);

        // 3rd iteration – D becomes ready
        await engine.RunOnceAsync();
        var d = (await store.GetAllAsync()).First(t => t.Title == "D");
        d.Status.Should().Be(CoreTaskStatus.Completed);
    }

    [TestMethod]
    public async Task RunOnceAsync_ShouldSkipTasks_WhenGoalIsPausedOrCancelled()
    {
        var goalPath = Path.Combine(Path.GetTempPath(), $"goals-skip-{Guid.NewGuid()}.json");
        var goalStore = new InMemoryGoalStore(goalPath);
        var goal = new Goal { Title = "Skip", Description = "X" };
        await goalStore.CreateAsync(goal);
        goal.Status = GoalStatus.Paused;
        await goalStore.UpdateAsync(goal);

        var store = new InMemoryTaskStore(filePath: null);
        var task = new ProjectTask { GoalId = goal.Id, Title = "X", Description = "X" };
        await store.CreateAsync(task);

        var engine = new TaskFlowEngine(store, new SimpleTaskExecutor(), goalStore, maxParallelism: 2);
        await engine.RunOnceAsync();

        var updated = await store.GetAsync(task.Id);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(CoreTaskStatus.Pending, "tasks whose goal is Paused should be skipped");

        goal.Status = GoalStatus.Cancelled;
        await goalStore.UpdateAsync(goal);
        await engine.RunOnceAsync();
        updated = await store.GetAsync(task.Id);
        updated!.Status.Should().Be(CoreTaskStatus.Pending, "tasks whose goal is Cancelled should be skipped");

        goal.Status = GoalStatus.InProgress;
        await goalStore.UpdateAsync(goal);
        await engine.RunOnceAsync();
        updated = await store.GetAsync(task.Id);
        updated!.Status.Should().Be(CoreTaskStatus.Completed, "tasks whose goal is InProgress should run");
    }
} 