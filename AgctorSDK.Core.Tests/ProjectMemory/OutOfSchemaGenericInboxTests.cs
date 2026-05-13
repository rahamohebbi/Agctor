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
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("I want to save"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("yes I consent"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("I consent"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("I consent to save it"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("I agree to store this"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("yes I wish to save it"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative,
            ConfirmationInputDetector.Classify("please store this fact"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Negative,
            ConfirmationInputDetector.Classify("no thanks"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.None,
            ConfirmationInputDetector.Classify("I do not consent to save it"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.None,
            ConfirmationInputDetector.Classify("please do not save it"));
        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.None,
            ConfirmationInputDetector.Classify("yes and also add his dog named Fido and also his car"));
    }

    [TestMethod]
    public async Task LlmConfirmationIntentClassifier_AlwaysAsksLlm_WhenPriorPromptExists()
    {
        var fakeLlm = new InlineLlm(label: "AFFIRMATIVE");
        var classifier = new LlmConfirmationIntentClassifier(fakeLlm);

        var phrases = new[]
        {
            "yes",
            "yes I consent",
            "please store this fact",
            "sounds great, please go ahead and write that down for me"
        };
        foreach (var phrase in phrases)
        {
            var signal = await classifier
                .ClassifyAsync(userMessage: phrase, lastAssistantPromptText: "Do you want me to store it?")
                .ConfigureAwait(false);
            Assert.AreEqual(
                ConfirmationInputDetector.ConfirmationSignal.Affirmative,
                signal,
                $"phrase: '{phrase}'");
        }

        Assert.AreEqual(phrases.Length, fakeLlm.CallCount,
            "LLM should classify every phrase when prior prompt context exists; heuristic is fallback only.");
        StringAssert.Contains(fakeLlm.LastPrompt ?? "", "AFFIRMATIVE: yes I consent",
            "Few-shot examples should ground the LLM with canonical AFFIRMATIVE phrases.");
        StringAssert.Contains(fakeLlm.LastPrompt ?? "", "NEGATIVE: please do not save it",
            "Few-shot examples should also include canonical NEGATIVE phrases for symmetry.");
    }

    [TestMethod]
    public async Task LlmConfirmationIntentClassifier_FallsBackToHeuristic_WhenLlmThrows()
    {
        var classifier = new LlmConfirmationIntentClassifier(new ThrowingLlm());
        var signal = await classifier
            .ClassifyAsync(userMessage: "yes", lastAssistantPromptText: "Do you want me to store it?")
            .ConfigureAwait(false);

        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.Affirmative, signal,
            "Heuristic must keep working when the LLM call fails (Ollama down, network issue, etc.).");
    }

    [TestMethod]
    public async Task LlmConfirmationIntentClassifier_ReturnsNone_WhenNoPriorAssistantPrompt()
    {
        var fakeLlm = new InlineLlm(label: "AFFIRMATIVE");
        var classifier = new LlmConfirmationIntentClassifier(fakeLlm);
        // Phrase deliberately ambiguous: without prior prompt we should not invent consent.
        var signal = await classifier
            .ClassifyAsync(userMessage: "sounds good to me, you decide", lastAssistantPromptText: null)
            .ConfigureAwait(false);

        Assert.AreEqual(ConfirmationInputDetector.ConfirmationSignal.None, signal);
        Assert.AreEqual(0, fakeLlm.CallCount, "Without prior prompt context, classifier must not invent consent.");
    }

    private sealed class InlineLlm : IProjectMemoryLlmClient
    {
        private readonly string _label;
        public int CallCount { get; private set; }
        public string? LastPrompt { get; private set; }

        public InlineLlm(string label) { _label = label; }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrompt = prompt;
            return Task.FromResult(_label);
        }
    }

    private sealed class ThrowingLlm : IProjectMemoryLlmClient
    {
        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new InvalidOperationException("LLM unavailable"));
    }

    [TestMethod]
    public async Task GenericInboxStore_AppendPending_DuplicateRefreshesConfirmationWindow()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pm-generic-inbox-refresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, ".agctor", "runtime"));
            var store = new GenericInboxStore();
            var intent = new MemoryIntent
            {
                EntityKey = "raha",
                KnowledgeType = "profile_fact",
                Attribute = "location",
                Value = "California, Irvine city",
                Confidence = 0.9
            };
            var pid = OutOfSchemaProposalFactory.ComputeProposalId(intent);
            var first = new OutOfSchemaFactProposal
            {
                ProposalId = pid,
                EntityKey = intent.EntityKey,
                KnowledgeType = intent.KnowledgeType,
                Attribute = intent.Attribute,
                Value = intent.Value,
                Confidence = intent.Confidence,
                Disposition = OutOfSchemaDisposition.ImmediateConfirmation,
                UserPromptLine = "old prompt"
            };
            await store.AppendPendingAsync(temp, scenarioSegment: "old_scenario", new[] { first }).ConfigureAwait(false);

            var pendingPath = GenericInboxPaths.PendingFile(temp);
            var oldYaml = await File.ReadAllTextAsync(pendingPath).ConfigureAwait(false);
            oldYaml = System.Text.RegularExpressions.Regex.Replace(
                oldYaml,
                "queuedAtUtc: .*",
                "queuedAtUtc: 2000-01-01T00:00:00.0000000+00:00");
            await File.WriteAllTextAsync(pendingPath, oldYaml).ConfigureAwait(false);

            var refreshed = new OutOfSchemaFactProposal
            {
                ProposalId = pid,
                EntityKey = intent.EntityKey,
                KnowledgeType = intent.KnowledgeType,
                Attribute = intent.Attribute,
                Value = intent.Value,
                Confidence = intent.Confidence,
                Disposition = OutOfSchemaDisposition.ImmediateConfirmation,
                UserPromptLine = "new prompt"
            };
            await store.AppendPendingAsync(temp, scenarioSegment: "person_1", new[] { refreshed }).ConfigureAwait(false);

            var pending = await store.LoadPendingAsync(temp).ConfigureAwait(false);
            Assert.AreEqual(1, pending.Count, "Duplicate proposal IDs should refresh in place, not append another row.");
            Assert.AreEqual("person_1", pending[0].ScenarioSegment);
            Assert.AreEqual("new prompt", pending[0].UserPromptLine);
            Assert.IsTrue(DateTimeOffset.TryParse(pending[0].QueuedAtUtc, out var queuedAt));
            Assert.IsTrue(queuedAt > DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2),
                "Repeated out-of-schema prompts should reopen the short confirmation window.");
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
            Assert.IsTrue(second.FinalText.Contains("routing", StringComparison.OrdinalIgnoreCase),
                "User should see that routing rules were auto-updated.");
            StringAssert.Contains(second.FinalText, "Workspace: /Dashboard/ProjectMemory/Workspace?path=");

            var routingRules = Path.Combine(temp, ".agctor", "schemas", "people", "routing-rules.yaml");
            var routingText = await File.ReadAllTextAsync(routingRules).ConfigureAwait(false);
            StringAssert.Contains(routingText, "pets");
            StringAssert.Contains(routingText, "dogs");

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
    public async Task RunAsync_Confirmation_UsesLatestScenarioPending_WhenWindowExpired()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-confirm-stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var inboxDir = Path.Combine(temp, ".agctor", "runtime", "generic-inbox");
            if (Directory.Exists(inboxDir))
                Directory.Delete(inboxDir, recursive: true);

            var store = new GenericInboxStore();
            var intent = new MemoryIntent
            {
                EntityKey = "raha",
                KnowledgeType = "profile_fact",
                Attribute = "location",
                Value = "California, Irvine city",
                Confidence = 0.9
            };
            var proposal = new OutOfSchemaFactProposal
            {
                ProposalId = OutOfSchemaProposalFactory.ComputeProposalId(intent),
                EntityKey = intent.EntityKey,
                KnowledgeType = intent.KnowledgeType,
                Attribute = intent.Attribute,
                Value = intent.Value,
                Confidence = intent.Confidence,
                Disposition = OutOfSchemaDisposition.ImmediateConfirmation,
                UserPromptLine = "test"
            };
            await store.AppendPendingAsync(temp, scenarioSegment: "person_1", new[] { proposal }).ConfigureAwait(false);

            var pendingPath = GenericInboxPaths.PendingFile(temp);
            var pendingText = await File.ReadAllTextAsync(pendingPath).ConfigureAwait(false);
            pendingText = System.Text.RegularExpressions.Regex.Replace(
                pendingText,
                "queuedAtUtc: .*",
                "queuedAtUtc: 2000-01-01T00:00:00.0000000+00:00");
            await File.WriteAllTextAsync(pendingPath, pendingText).ConfigureAwait(false);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new FakeLlm());
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                ScenarioId = "person_1",
                UserMessage = "yes I consent",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);
            StringAssert.Contains(result.FinalText, "Stored 1 fact");
            StringAssert.Contains(result.FinalText, "profile_fact/location");
            Assert.IsTrue(result.Steps.Any(s => s.Name == "confirm-window"));
            Assert.IsTrue(File.Exists(GenericInboxPaths.ConfirmedFile(temp)));
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

    [TestMethod]
    public void ProjectMemoryDashboardPaths_WorkspaceFileHref_EncodesRelativePath()
    {
        var href = ProjectMemoryDashboardPaths.WorkspaceFileHref(".agctor/schemas/people/routing-rules.yaml");
        StringAssert.StartsWith(href, "/Dashboard/ProjectMemory/Workspace?path=");
        Assert.IsTrue(href.Contains("%2F", StringComparison.Ordinal), "slashes must be encoded in the query string");
    }
}
