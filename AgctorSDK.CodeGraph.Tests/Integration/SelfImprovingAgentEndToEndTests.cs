using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Agents;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using AgctorSDK.CodeGraph.Embeddings;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.CodeGraph.Snapshots;
using AgctorSDK.Core.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Integration
{
    /// <summary>
    /// End-to-end workflow that stitches together indexing, vector search, test planning/scaffolding and code review.
    /// It proves the individual Group-1 → Group-6 pieces interoperate as a pipeline.
    /// </summary>
    [TestClass]
    public class SelfImprovingAgentEndToEndTests
    {
        [TestMethod]
        public async Task SelfImprovingAgent_ShouldUnderstandIndexAndSuggestPR()
        {
            // 1) Build initial solution graph without the new feature and snapshot it
            var repoDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(repoDir);
            var registry = new AnalyzerRegistry();
            registry.RegisterAnalyzer(new RoslynCodeAnalyzer());
            var generator = new StubEmbeddingGenerator();
            var vectorStore = new InMemoryVectorStore();
            var storeActor = new EmbeddingStoreActor("vec", vectorStore);

            var solV1 = BuildSolution(includeEmailVerification:false);
            var snap1Path = await SnapshotService.SaveSnapshotAsync(solV1, repoDir, "before");

            // 2) Modify graph to include new behaviour (RegisterUser sends verification)
            var solV2 = BuildSolution(includeEmailVerification:true);
            var snap2Path = await SnapshotService.SaveSnapshotAsync(solV2, repoDir, "after");

            // 3) Diff snapshots as GitWatcher would do
            var snap1 = await SnapshotService.LoadSnapshotAsync(snap1Path);
            var snap2 = await SnapshotService.LoadSnapshotAsync(snap2Path);
            var diff = SnapshotDiffService.Diff(snap1, snap2, registry);
            Assert.IsTrue(diff.AddedMethods.Any(m => m.Contains("SendVerificationEmail")), "Diff should capture new method");

            // 4) Index embeddings for new graph so planner & search can work
            var indexer = new IndexerAgent("idx", registry, generator, storeActor);
            await indexer.IndexAsync(solV2);
            Assert.IsTrue(await vectorStore.CountAsync() > 0, "Embeddings should exist after indexing");

            // 5) Use TestPlannerAgent to create failing tests
            var planner = new TestPlannerAgent("planner", repoDir);
            var planEnv = await planner.ReceiveAsync(new MessageEnvelope(new PlanTestsMessage(diff)));
            var plan = (TestPlanResult)planEnv.Payload;
            Assert.IsTrue(plan.Tasks.Any(), "Planner should output at least one test task");

            // 6) Scaffold tests
            var scaffolder = new TestScaffolderActor("scaff");
            foreach (var task in plan.Tasks)
            {
                var resp = await scaffolder.ReceiveAsync(new MessageEnvelope(new ScaffoldTestMessage(task)));
                var scaffold = (TestScaffoldedMessage)resp.Payload;
                Assert.IsTrue(File.Exists(scaffold.FilePath), $"Scaffolded file {scaffold.FilePath} should exist");
            }

            // 7) Code review of the diff
            var stubLlm = new StubLlm("PR looks good – score 8/10");
            var reviewer = new CodeReviewerAgent("rev", stubLlm);
            var reviewEnv = await reviewer.ReceiveAsync(new MessageEnvelope(new ReviewCommitMessage("after", diff)));
            var review = (CodeReviewResult)reviewEnv.Payload;
            Assert.IsTrue(review.Score >= 8, "Expected high review score");

            // 8) Simulate PR packaging (simple object)
            var pr = new
            {
                Diff = diff,
                Tests = plan.Tasks.Select(t => Path.GetFileName(t.TestFilePath)).ToList(),
                ReviewSummary = review.Summary,
                Score = review.Score
            };
            Assert.IsTrue(pr.Tests.Any(), "PR should include new tests");
            StringAssert.Contains(pr.ReviewSummary, "score", "Review summary should originate from LLM response");
        }

        private static SolutionActor BuildSolution(bool includeEmailVerification)
        {
            var sol = new SolutionActor("Sol", "Sol.sln");
            var proj = new ProjectActor("Core", "Core.csproj");
            sol.AddProject(proj);
            var file = new FileActor("AuthService.cs", "AuthService.cs");
            proj.AddFile(file);
            var cls = new ClassActor("AuthService");
            file.AddClass(cls);
            // Always have RegisterUser
            var reg = new MethodActor("RegisterUser");
            cls.AddMethod(reg);
            if (includeEmailVerification)
            {
                cls.AddMethod(new MethodActor("SendVerificationEmail"));
            }
            return sol;
        }

        // Re-use stub embedding generator from previous tests
        private class StubEmbeddingGenerator : IEmbeddingGenerator
        {
            public Task<float[]> GenerateEmbeddingAsync(string text)
            {
                int h = text.GetHashCode();
                return Task.FromResult(new float[] { ((h>>0)&0xFF)/255f, ((h>>8)&0xFF)/255f, ((h>>16)&0xFF)/255f });
            }
        }

        private class StubLlm : ILlmClient
        {
            private readonly string _resp;
            public string? LastPrompt { get; private set; }
            public StubLlm(string resp) { _resp = resp; }
            public Task<string> CompleteAsync(string prompt, LlmOptions? options = null)
            {
                LastPrompt = prompt;
                return Task.FromResult(_resp);
            }
        }
    }
} 