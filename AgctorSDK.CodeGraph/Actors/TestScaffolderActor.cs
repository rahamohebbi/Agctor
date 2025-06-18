using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Writes skeleton test files for the provided <see cref="TestTask"/>.
    /// </summary>
    public sealed class TestScaffolderActor : Agent
    {
        public TestScaffolderActor(string id) : base(id) {}

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is ScaffoldTestMessage scaffold)
            {
                var filePath = await WriteSkeletonAsync(scaffold.Task);
                return envelope.WithPayload(new TestScaffoldedMessage(filePath));
            }
            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        private static async Task<string> WriteSkeletonAsync(TestTask task)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(task.TestFilePath)!);
            var ns = Path.GetFileNameWithoutExtension(task.TestProjectPath);
            var content = $@"using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace {ns}
{{
    [TestClass]
    public class {task.ClassName}Tests
    {{
        [TestMethod]
        public void {task.MethodName}_ShouldDoSomething()
        {{
            // TODO: implement test
            Assert.Fail(""Not implemented"");
        }}
    }}
}}";
            await File.WriteAllTextAsync(task.TestFilePath, content);
            return task.TestFilePath;
        }
    }
} 