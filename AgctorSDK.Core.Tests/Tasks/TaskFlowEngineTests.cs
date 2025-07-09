using System;
using System.Linq;
using System.Threading.Tasks;
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
} 