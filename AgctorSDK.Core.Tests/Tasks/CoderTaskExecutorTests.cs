using System;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.Coding;
using AgctorSDK.Core.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreTaskStatus = AgctorSDK.Core.Tasks.TaskStatus;

namespace AgctorSDK.Core.Tests.Tasks;

[TestClass]
public class CoderTaskExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_CompletesTask_AndWritesFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid()}");
        var generator = new SimpleCodeGenerator(tempDir);
        var executor = new CoderTaskExecutor(generator);

        var task = new ProjectTask
        {
            GoalId = Guid.NewGuid(),
            Title = "HelloWorld",
            Description = "Generate hello world stub"
        };

        await executor.ExecuteAsync(task);
        task.Status.Should().Be(CoreTaskStatus.Completed);

        // One file should exist in tempDir
        Directory.GetFiles(tempDir).Should().ContainSingle();
    }
} 