using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

/// <summary>
/// PRD-019 Option B: replay <c>confirmed.yaml</c> through current routing rules so previously
/// out-of-schema facts land on entity files (e.g. <c>raha/profile.md</c>) once a matching rule exists.
/// </summary>
[TestClass]
public sealed class GenericInboxReplayTests
{
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

    private sealed class FakeLlm : IProjectMemoryLlmClient
    {
        public Task<string> GenerateAsync(string prompt, System.Threading.CancellationToken cancellationToken = default) =>
            Task.FromResult("");
    }

    [TestMethod]
    public async Task Replay_RoutesConfirmedRow_AndMarksReplayed_WhenRuleMatches()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-replay-routes-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            // Reset inbox so the test only sees its own row.
            var inboxDir = Path.Combine(temp, ".agctor", "runtime", "generic-inbox");
            if (Directory.Exists(inboxDir)) Directory.Delete(inboxDir, recursive: true);

            // Bootstrap raha's folder under person_1 so projection has a target file.
            // profile.md must contain the schema's "Basic Info" section header so DocumentProjectionService can replace.
            var personRoot = Path.Combine(temp, "scenarios", "person_1", "people", "raha");
            Directory.CreateDirectory(personRoot);
            File.WriteAllText(Path.Combine(personRoot, "entity.yaml"),
                "entityKey: raha\nentityType: person\nmetadata:\n  displayName: Raha\n");
            File.WriteAllText(Path.Combine(personRoot, "profile.md"),
                "# Profile\n\n## Basic Info\n\n## Physical Attributes\n\n## Roles\n\n## Notes\n");

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new FakeLlm());
            var sp = services.BuildServiceProvider();
            var store = sp.GetRequiredService<IGenericInboxStore>();
            var replay = sp.GetRequiredService<IGenericInboxReplayService>();

            // Append a routable confirmed row directly: profile_fact/education already routes to profile/Basic Info.
            var intent = new MemoryIntent
            {
                EntityKey = "raha",
                KnowledgeType = "profile_fact",
                Attribute = "education",
                Value = "Computer Science degree from the University of Salford in the UK",
                Confidence = 1.0
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
                Disposition = OutOfSchemaDisposition.ImmediateConfirmation,
                UserPromptLine = "test"
            };
            await store.AppendPendingAsync(temp, scenarioSegment: "person_1", new[] { proposal }).ConfigureAwait(false);
            await store.PersistApprovedAsync(temp, scenarioSegment: "person_1", new[]
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
            }).ConfigureAwait(false);

            var report = await replay.ReplayAsync(temp, scenarioId: "person_1").ConfigureAwait(false);

            Assert.AreEqual(1, report.Considered);
            Assert.AreEqual(1, report.Routed);
            Assert.AreEqual(0, report.SkippedRouteMiss);
            Assert.IsTrue(report.UpdatedFiles.Count > 0, "Routed row should write to profile.md");

            var profile = await File.ReadAllTextAsync(Path.Combine(personRoot, "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(profile, "Education", "profile.md should now contain the back-filled education fact.");

            // Confirm row should be stamped with a real ISO timestamp so the next replay is idempotent.
            var confirmedYaml = await File.ReadAllTextAsync(GenericInboxPaths.ConfirmedFile(temp)).ConfigureAwait(false);
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(confirmedYaml, @"replayedAtUtc:\s*20\d\d-"),
                "Routed rows must have replayedAtUtc stamped with a real timestamp.");

            var second = await replay.ReplayAsync(temp, scenarioId: "person_1").ConfigureAwait(false);
            Assert.AreEqual(0, second.Considered, "Second replay should skip already-replayed rows.");
            Assert.AreEqual(1, second.SkippedAlreadyReplayed);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task Replay_LeavesUnroutedRow_AndReportsRouteMiss()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-replay-routemiss-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var inboxDir = Path.Combine(temp, ".agctor", "runtime", "generic-inbox");
            if (Directory.Exists(inboxDir)) Directory.Delete(inboxDir, recursive: true);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new FakeLlm());
            var sp = services.BuildServiceProvider();
            var store = sp.GetRequiredService<IGenericInboxStore>();
            var replay = sp.GetRequiredService<IGenericInboxReplayService>();

            var intent = new MemoryIntent
            {
                EntityKey = "raha",
                // No rule for `unknown_kt` exists in the sample project schema.
                KnowledgeType = "unknown_kt",
                Attribute = "anything",
                Value = "value",
                Confidence = 0.9
            };
            var pid = OutOfSchemaProposalFactory.ComputeProposalId(intent);
            await store.AppendPendingAsync(temp, scenarioSegment: "person_1", new[]
            {
                new OutOfSchemaFactProposal
                {
                    ProposalId = pid,
                    EntityKey = intent.EntityKey,
                    KnowledgeType = intent.KnowledgeType,
                    Attribute = intent.Attribute,
                    Value = intent.Value,
                    Confidence = intent.Confidence,
                    Disposition = OutOfSchemaDisposition.ImmediateConfirmation,
                    UserPromptLine = "test"
                }
            }).ConfigureAwait(false);
            await store.PersistApprovedAsync(temp, scenarioSegment: "person_1", new[]
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
            }).ConfigureAwait(false);

            var report = await replay.ReplayAsync(temp, scenarioId: "person_1").ConfigureAwait(false);

            Assert.AreEqual(1, report.Considered);
            Assert.AreEqual(0, report.Routed);
            Assert.AreEqual(1, report.SkippedRouteMiss, "Row stays in confirmed.yaml until the schema gains a matching rule.");
            Assert.IsTrue(report.UpdatedFiles.Count == 0);

            var confirmedYaml = await File.ReadAllTextAsync(GenericInboxPaths.ConfirmedFile(temp)).ConfigureAwait(false);
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(confirmedYaml, @"replayedAtUtc:\s*20\d\d-"),
                "route_miss rows must not be stamped with a real replayedAtUtc timestamp.");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task Replay_ScenarioFilter_OnlyTouchesMatchingScope()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-replay-scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var inboxDir = Path.Combine(temp, ".agctor", "runtime", "generic-inbox");
            if (Directory.Exists(inboxDir)) Directory.Delete(inboxDir, recursive: true);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new FakeLlm());
            var sp = services.BuildServiceProvider();
            var store = sp.GetRequiredService<IGenericInboxStore>();
            var replay = sp.GetRequiredService<IGenericInboxReplayService>();

            // Two confirmed rows under different scenarios; only person_1 should be replayed.
            var intentA = new MemoryIntent { EntityKey = "raha", KnowledgeType = "profile_fact", Attribute = "education", Value = "A", Confidence = 1.0 };
            var intentB = new MemoryIntent { EntityKey = "raha", KnowledgeType = "profile_fact", Attribute = "education", Value = "B", Confidence = 1.0 };

            await SeedConfirmedAsync(store, temp, "person_1", intentA).ConfigureAwait(false);
            await SeedConfirmedAsync(store, temp, "person_2", intentB).ConfigureAwait(false);

            var report = await replay.ReplayAsync(temp, scenarioId: "person_1").ConfigureAwait(false);
            Assert.AreEqual(1, report.Considered);
            // Row B should not be touched.
            var confirmedYaml = await File.ReadAllTextAsync(GenericInboxPaths.ConfirmedFile(temp)).ConfigureAwait(false);
            var aId = OutOfSchemaProposalFactory.ComputeProposalId(intentA);
            var bId = OutOfSchemaProposalFactory.ComputeProposalId(intentB);
            // A may or may not have replayedAtUtc depending on entity discovery; but B must remain unmarked.
            var bSection = ExtractRow(confirmedYaml, bId);
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(bSection, @"replayedAtUtc:\s*20\d\d-"),
                "Rows in a different scenario must not be stamped with a real replayedAtUtc timestamp by a scoped replay.");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    private static async Task SeedConfirmedAsync(IGenericInboxStore store, string temp, string scenarioSegment, MemoryIntent intent)
    {
        var pid = OutOfSchemaProposalFactory.ComputeProposalId(intent);
        await store.AppendPendingAsync(temp, scenarioSegment, new[]
        {
            new OutOfSchemaFactProposal
            {
                ProposalId = pid,
                EntityKey = intent.EntityKey,
                KnowledgeType = intent.KnowledgeType,
                Attribute = intent.Attribute,
                Value = intent.Value,
                Confidence = intent.Confidence,
                Disposition = OutOfSchemaDisposition.ImmediateConfirmation,
                UserPromptLine = "test"
            }
        }).ConfigureAwait(false);

        await store.PersistApprovedAsync(temp, scenarioSegment, new[]
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
        }).ConfigureAwait(false);
    }

    private static string ExtractRow(string yaml, string proposalId)
    {
        var idx = yaml.IndexOf("proposalId: " + proposalId, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var nextIdx = yaml.IndexOf("- proposalId:", idx + 1, StringComparison.OrdinalIgnoreCase);
        return nextIdx < 0 ? yaml[idx..] : yaml[idx..nextIdx];
    }
}
