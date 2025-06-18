using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.CodeGraph.Snapshots;
using AgctorSDK.Core.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Review
{
    public class StubLlm : ILlmClient
    {
        public Task<string> CompleteAsync(string prompt, LlmOptions? options = null) => Task.FromResult("LGTM! score 9/10");
    }

    [TestClass]
    public class CodeReviewerAgentTests
    {
        [TestMethod]
        public async Task Reviewer_ReturnsSummary()
        {
            var diff = new SnapshotDiffResult();
            diff.AddedMethods.Add("Foo.Bar");
            var agent = new CodeReviewerAgent("rev", new StubLlm());
            var reply = await agent.ReceiveAsync(new MessageEnvelope(new ReviewCommitMessage("abc", diff)));
            var res = (CodeReviewResult)reply.Payload;
            StringAssert.Contains(res.Summary, "LGTM");
            Assert.IsTrue(res.Score <= 10);
        }
    }
} 