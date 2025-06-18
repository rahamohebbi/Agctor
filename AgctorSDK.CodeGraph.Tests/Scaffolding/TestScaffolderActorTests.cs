using System.IO;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.Core.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Scaffolding
{
    [TestClass]
    public class TestScaffolderActorTests
    {
        [TestMethod]
        public async Task Scaffolder_CreatesTestFile()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var task = new TestTask("Foo", "Bar", "Foo.cs", tempDir, Path.Combine(tempDir, "FooTests.cs"));
            var actor = new TestScaffolderActor("scaff");

            var reply = await actor.ReceiveAsync(new MessageEnvelope(new ScaffoldTestMessage(task)));
            var path = ((TestScaffoldedMessage)reply.Payload).FilePath;
            Assert.IsTrue(File.Exists(path));
            var content = await File.ReadAllTextAsync(path);
            StringAssert.Contains(content, "class FooTests");
            StringAssert.Contains(content, "Bar_ShouldDoSomething");
        }
    }
} 