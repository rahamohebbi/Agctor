using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.CodeGraph.Snapshots;
using AgctorSDK.Core.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Integration
{
    internal sealed class RecordingStubLlm : ILlmClient
    {
        public string? LastPrompt { get; private set; }
        private readonly string _response;
        public RecordingStubLlm(string response)
        {
            _response = response;
        }
        public Task<string> CompleteAsync(string prompt, LlmOptions? options = null)
        {
            LastPrompt = prompt;
            return Task.FromResult(_response);
        }
    }

    /// <summary>
    /// Group-6 integration – validates that CodeReviewerAgent formats the diff, calls the LLM and returns a structured review.
    /// </summary>
    [TestClass]
    public class CodeReviewIntegrationTests
    {
        [TestMethod]
        public async Task CodeReviewerAgent_ShouldSummarizeDiffAndScore()
        {
            // Arrange – create a diff with one added method so internal scoring returns 9.
            var diff = new SnapshotDiffResult();
            diff.AddedMethods.Add("AuthService.RegisterUser");

            var stub = new RecordingStubLlm("Looks good overall.\nPros: ...\nCons: ...");
            var agent = new CodeReviewerAgent("rev", stub);

            // Act
            var env = await agent.ReceiveAsync(new MessageEnvelope(new ReviewCommitMessage("commit123", diff)));
            var review = (CodeReviewResult)env.Payload;

            // Assert – LLM was invoked with formatted diff
            Assert.IsNotNull(stub.LastPrompt, "LLM should have been called");
            StringAssert.Contains(stub.LastPrompt!, "Added Methods:");
            StringAssert.Contains(stub.LastPrompt!, "AuthService.RegisterUser");

            // Review result echoes stub summary and computed score of 9
            StringAssert.Contains(review.Summary, "Looks good overall");
            Assert.AreEqual(9, review.Score);
            Assert.AreEqual(0, review.Comments.Count);
        }
    }
} 