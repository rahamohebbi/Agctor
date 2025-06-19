using System.IO;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.CodeGraph.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AgctorSDK.Core.Messages;
using System.Linq;

namespace AgctorSDK.CodeGraph.Tests.Integration
{
    /// <summary>
    /// Group-5 integration – validates that a TestPlannerAgent creates tasks which the TestScaffolderActor
    /// turns into physical MSTest skeleton files.
    /// </summary>
    [TestClass]
    public class TestScaffoldingFlowTests
    {
        [TestMethod]
        public async Task InjectTest_ShouldCreateTestTemplate()
        {
            // Arrange – fake solution dir with Tests project folder
            var solutionDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var testsProjectDir = Path.Combine(solutionDir, "AgctorSDK.Core.Tests");
            Directory.CreateDirectory(testsProjectDir);

            // Diff indicates a new method MyService.RegisterUser
            var diff = new AgctorSDK.CodeGraph.Snapshots.SnapshotDiffResult();
            diff.AddedMethods.Add("MyService.RegisterUser");

            var planner = new TestPlannerAgent("planner", solutionDir);
            var planEnv = await planner.ReceiveAsync(new MessageEnvelope(new PlanTestsMessage(diff)));
            var plan = (TestPlanResult)planEnv.Payload;
            Assert.AreEqual(1, plan.Tasks.Count, "Planner should return one TestTask");
            var task = plan.Tasks.First();

            // Act – feed task to scaffolder
            var scaffolder = new TestScaffolderActor("scaff");
            var scaffoldEnv = await scaffolder.ReceiveAsync(new MessageEnvelope(new ScaffoldTestMessage(task)));
            var scaffoldRes = (TestScaffoldedMessage)scaffoldEnv.Payload;

            // Assert – file exists and contains template code
            Assert.IsTrue(File.Exists(scaffoldRes.FilePath), "Scaffolded test file should exist on disk");
            var content = await File.ReadAllTextAsync(scaffoldRes.FilePath);
            StringAssert.Contains(content, "class " + task.ClassName + "Tests");
            StringAssert.Contains(content, task.MethodName + "_ShouldDoSomething");
        }
    }
} 