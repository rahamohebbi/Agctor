using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class OutOfSchemaGenericInboxTests
{
    private sealed class FakeLlm : IProjectMemoryLlmClient
    {
        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult("");
    }

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static void CopyDir(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            var d = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(d))
                Directory.CreateDirectory(d);
            File.Copy(file, target, overwrite: true);
        }
    }

    [TestMethod]
    public void ProposalFactory_RouteMiss_BuildsImmediate_WhenHighConfidence()
    {
        var intent = new MemoryIntent
        {
            EntityKey = "raha",
            KnowledgeType = "profile_fact",
            Attribute = "unknown_attr",
            Value = "x",
            Confidence = 0.99
        };
        var issues = new List<ValidationIssue>
        {
            new()
            {
                Code = "route_miss",
                Message = "miss",
                IsError = true,
                RelatedIntent = intent
            }
        };
        var proposals = OutOfSchemaProposalFactory.FromRouteIssues(issues, options: null);
        Assert.AreEqual(1, proposals.Count);
        Assert.AreEqual(OutOfSchemaDisposition.ImmediateConfirmation, proposals[0].Disposition);
        Assert.IsFalse(string.IsNullOrWhiteSpace(proposals[0].ProposalId));
        StringAssert.Contains(proposals[0].UserPromptLine, "not covered");
    }

    [TestMethod]
    public void ProposalFactory_RouteMiss_BuildsReviewQueue_WhenMidConfidence()
    {
        var intent = new MemoryIntent
        {
            EntityKey = "raha",
            KnowledgeType = "pets",
            Attribute = "dogs",
            Value = "two",
            Confidence = 0.5
        };
        var issues = new List<ValidationIssue>
        {
            new() { Code = "route_miss", Message = "miss", IsError = true, RelatedIntent = intent }
        };
        var proposals = OutOfSchemaProposalFactory.FromRouteIssues(issues, new OutOfSchemaCaptureOptions());
        Assert.AreEqual(1, proposals.Count);
        Assert.AreEqual(OutOfSchemaDisposition.ReviewQueue, proposals[0].Disposition);
    }

    [TestMethod]
    public void ProposalFactory_DropsBelowReviewThreshold()
    {
        var intent = new MemoryIntent
        {
            EntityKey = "raha",
            KnowledgeType = "pets",
            Attribute = "dogs",
            Value = "two",
            Confidence = 0.1
        };
        var issues = new List<ValidationIssue>
        {
            new() { Code = "route_miss", Message = "miss", IsError = true, RelatedIntent = intent }
        };
        var proposals = OutOfSchemaProposalFactory.FromRouteIssues(issues, new OutOfSchemaCaptureOptions());
        Assert.AreEqual(0, proposals.Count);
    }

    [TestMethod]
    public async Task GenericInboxStore_AppendPending_ThenPersistApproved_MovesRow()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pm-generic-inbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, ".agctor", "runtime"));
            var store = new GenericInboxStore();
            var intent = new MemoryIntent
            {
                EntityKey = "raha",
                KnowledgeType = "pets",
                Attribute = "dogs",
                Value = "two",
                Confidence = 0.5
            };
            var pid = OutOfSchemaProposalFactory.ComputeProposalId(intent);
            var proposal = new OutOfSchemaFactProposal
            {
                ProposalId = pid,
                EntityKey = intent.EntityKey,
                KnowledgeType = intent.KnowledgeType,
                Attribute = intent.Attribute,
                Value = intent.Value,
                Confidence = intent.Confidence,
                Disposition = OutOfSchemaDisposition.ReviewQueue,
                UserPromptLine = "test"
            };
            await store.AppendPendingAsync(temp, scenarioSegment: null, new[] { proposal }).ConfigureAwait(false);
            var pendingPath = GenericInboxPaths.PendingFile(temp);
            Assert.IsTrue(File.Exists(pendingPath));
            var pendingYaml = await File.ReadAllTextAsync(pendingPath).ConfigureAwait(false);
            StringAssert.Contains(pendingYaml, pid);

            var res = await store.PersistApprovedAsync(
                    temp,
                    scenarioSegment: null,
                    new[]
                    {
                        new ApprovedGenericFact
                        {
                            ProposalId = pid,
                            EntityKey = intent.EntityKey,
                            KnowledgeType = intent.KnowledgeType,
                            Attribute = intent.Attribute,
                            Value = intent.Value,
                            Confidence = intent.Confidence
                        }
                    })
                .ConfigureAwait(false);
            Assert.AreEqual(1, res.Appended);
            Assert.AreEqual(0, res.RejectedMismatch);

            var confirmed = await File.ReadAllTextAsync(GenericInboxPaths.ConfirmedFile(temp)).ConfigureAwait(false);
            StringAssert.Contains(confirmed, pid);
            var pendingAfter = await File.ReadAllTextAsync(pendingPath).ConfigureAwait(false);
            Assert.IsFalse(pendingAfter.Contains(pid));
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    [TestMethod]
    public async Task IngestFromExtractorOutputAsync_HighConfidenceRouteMiss_IsImmediate_NoPendingFile()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-oos-immediate-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var json =
                """
                {
                  "memoryIntents":[
                    {"entityKey":"raha","knowledgeType":"profile_fact","attribute":"age","value":"45","confidence":0.99},
                    {"entityKey":"raha","knowledgeType":"profile_fact","attribute":"unknown_attr","value":"x","confidence":0.99}
                  ]
                }
                """;

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new FakeLlm());
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var ingest = await runner.IngestFromExtractorOutputAsync(temp, scenarioId: null, json).ConfigureAwait(false);
            Assert.IsTrue(ingest.ParseSuccess, ingest.Summary);
            Assert.AreEqual(1, ingest.OutOfSchemaProposals.Count);
            Assert.AreEqual(OutOfSchemaDisposition.ImmediateConfirmation, ingest.OutOfSchemaProposals[0].Disposition);
            var pending = Path.Combine(temp, ".agctor", "runtime", "generic-inbox", "pending.yaml");
            Assert.IsTrue(File.Exists(pending), "All proposals (incl. immediate) now persist to pending.yaml so a later 'yes' can honor them.");
            var pendingText = await File.ReadAllTextAsync(pending).ConfigureAwait(false);
            StringAssert.Contains(pendingText, "immediate");
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    [TestMethod]
    public void ConfirmationDetector_ReturnsAffirmative_ForShortYes()
    {
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("yes"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("Yes please"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("store it"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("store this fact."));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Negative,
            ConfirmationInputDetector.Classify("no thanks"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.None,
            ConfirmationInputDetector.Classify("yes and also add his dog named Fido and also his car"));
    }

    [TestMethod]
    public async Task RunAsync_Confirmation_Yes_PersistsPendingImmediateRow()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-confirm-yes-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            var llm = new ProjectMemoryPipelineRunnerTests.QueueLlm();
            llm.Responses.Enqueue(
                """
                {"memoryIntents":[{"entityKey":"raha","knowledgeType":"pets","attribute":"dogs","value":"two dogs","confidence":0.9}]}
                """);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var sp = services.BuildServiceProvider();
            var runner = sp.GetRequiredService<IProjectMemoryPipelineRunner>();

            var first = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "Raha has two dogs.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);
            Assert.IsTrue(first.Success, first.FinalText);

            var pending = Path.Combine(temp, ".agctor", "runtime", "generic-inbox", "pending.yaml");
            Assert.IsTrue(File.Exists(pending), "Immediate-band proposal must persist to pending.yaml now.");

            var second = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "store this fact.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);
            Assert.IsTrue(second.Success, second.FinalText);
            StringAssert.Contains(second.FinalText, "Stored");

            var confirmedPath = Path.Combine(temp, ".agctor", "runtime", "generic-inbox", "confirmed.yaml");
            Assert.IsTrue(File.Exists(confirmedPath));
            var confirmedText = await File.ReadAllTextAsync(confirmedPath).ConfigureAwait(false);
            StringAssert.Contains(confirmedText, "dogs");
            StringAssert.Contains(confirmedText, "two dogs");
            StringAssert.Contains(confirmedText, "raha");

            var pendingText = await File.ReadAllTextAsync(pending).ConfigureAwait(false);
            Assert.IsFalse(pendingText.Contains("two dogs"),
                "Promoted row should be removed from pending.yaml.");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task RunAsync_Confirmation_No_RemovesPendingWithoutPersisting()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-confirm-no-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            var llm = new ProjectMemoryPipelineRunnerTests.QueueLlm();
            llm.Responses.Enqueue(
                """
                {"memoryIntents":[{"entityKey":"raha","knowledgeType":"pets","attribute":"dogs","value":"two","confidence":0.9}]}
                """);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "Raha also has two dogs",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "no",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);
            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.FinalText, "will not store");

            var confirmedPath = Path.Combine(temp, ".agctor", "runtime", "generic-inbox", "confirmed.yaml");
            if (File.Exists(confirmedPath))
            {
                var text = await File.ReadAllTextAsync(confirmedPath).ConfigureAwait(false);
                Assert.IsFalse(text.Contains("dogs"));
            }

            var pending = Path.Combine(temp, ".agctor", "runtime", "generic-inbox", "pending.yaml");
            if (File.Exists(pending))
            {
                var text = await File.ReadAllTextAsync(pending).ConfigureAwait(false);
                Assert.IsFalse(text.Contains("dogs"));
            }
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task IngestFromExtractorOutputAsync_PartialRouteMiss_SurfacesOutOfSchema_AndQueuesReviewBand()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-oos-ingest-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var json =
                """
                {
                  "memoryIntents":[
                    {"entityKey":"raha","knowledgeType":"profile_fact","attribute":"age","value":"45","confidence":0.99},
                    {"entityKey":"raha","knowledgeType":"profile_fact","attribute":"unknown_attr","value":"x","confidence":0.5}
                  ]
                }
                """;

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new FakeLlm());
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var ingest = await runner.IngestFromExtractorOutputAsync(temp, scenarioId: null, json).ConfigureAwait(false);
            Assert.IsTrue(ingest.ParseSuccess, ingest.Summary);
            Assert.AreEqual(1, ingest.OutOfSchemaProposals.Count);
            Assert.AreEqual(OutOfSchemaDisposition.ReviewQueue, ingest.OutOfSchemaProposals[0].Disposition);

            var pending = Path.Combine(temp, ".agctor", "runtime", "generic-inbox", "pending.yaml");
            Assert.IsTrue(File.Exists(pending), "Review-queue band should persist to pending.yaml.");
            var pendingText = await File.ReadAllTextAsync(pending).ConfigureAwait(false);
            StringAssert.Contains(pendingText, ingest.OutOfSchemaProposals[0].ProposalId);

            var persist = await runner.PersistApprovedGenericFactsAsync(
                    temp,
                    scenarioId: null,
                    new[]
                    {
                        new ApprovedGenericFact
                        {
                            ProposalId = ingest.OutOfSchemaProposals[0].ProposalId,
                            EntityKey = "raha",
                            KnowledgeType = "profile_fact",
                            Attribute = "unknown_attr",
                            Value = "x",
                            Confidence = 0.5
                        }
                    })
                .ConfigureAwait(false);
            Assert.AreEqual(1, persist.Appended);
            Assert.IsFalse(File.ReadAllText(pending).Contains(ingest.OutOfSchemaProposals[0].ProposalId));
            StringAssert.Contains(await File.ReadAllTextAsync(GenericInboxPaths.ConfirmedFile(temp)).ConfigureAwait(false),
                ingest.OutOfSchemaProposals[0].ProposalId);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
            }
        }
    }
}
