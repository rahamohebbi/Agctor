using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class ProjectMemoryPipelineRunnerTests
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    internal sealed class QueueLlm : IProjectMemoryLlmClient
    {
        public readonly Queue<string> Responses = new();

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : "");
    }

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
    public async Task IngestOnly_ValidOccupationIntent_UpdatesProfileOnDisk()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        Assert.IsTrue(Directory.Exists(src), $"Missing sample at {src}");

        var temp = Path.Combine(Path.GetTempPath(), "pm-orchestrator-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            const string marker = "OrchestratorUnitTestOccupation";
            var llm = new QueueLlm();
            llm.Responses.Enqueue(
                $$"""{"memoryIntents":[{"entityKey":"raha","knowledgeType":"occupation","attribute":"","value":"{{marker}}","confidence":0.95}]}""");

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "Raha works as something new.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);
            var profile = await File.ReadAllTextAsync(Path.Combine(temp, "people", "raha", "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(profile, marker);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // best-effort cleanup on CI
            }
        }
    }

    [TestMethod]
    public async Task IngestOnly_WithScenarioId_WritesUnderScenariosFolder()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        Assert.IsTrue(Directory.Exists(src), $"Missing sample at {src}");

        var temp = Path.Combine(Path.GetTempPath(), "pm-orchestrator-scoped-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var peopleSrc = Path.Combine(temp, "people");
            var scenRoot = Path.Combine(temp, "scenarios", "demo-scen");
            Directory.CreateDirectory(scenRoot);
            Directory.Move(peopleSrc, Path.Combine(scenRoot, "people"));

            const string marker = "ScopedScenarioOccupation";
            var llm = new QueueLlm();
            llm.Responses.Enqueue(
                $$"""{"memoryIntents":[{"entityKey":"raha","knowledgeType":"occupation","attribute":"","value":"{{marker}}","confidence":0.95}]}""");

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "Raha works as something new.",
                Mode = ProjectMemoryPipelineMode.IngestOnly,
                ScenarioId = "demo-scen"
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);
            var scopedProfile = Path.Combine(temp, "scenarios", "demo-scen", "people", "raha", "profile.md");
            Assert.IsTrue(File.Exists(scopedProfile), "Expected scoped profile path.");
            var profile = await File.ReadAllTextAsync(scopedProfile).ConfigureAwait(false);
            StringAssert.Contains(profile, marker);
            Assert.IsFalse(File.Exists(Path.Combine(temp, "people", "raha", "profile.md")), "Legacy project-root people/ must not be used.");
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // best-effort cleanup on CI
            }
        }
    }

    /// <summary>API used after PRD-014 PersonaCall LLM — same scoped paths as full pipeline ingest.</summary>
    [TestMethod]
    public async Task IngestFromExtractorOutputAsync_WritesUnderScenarioWorkspace()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        Assert.IsTrue(Directory.Exists(src), $"Missing sample at {src}");

        var temp = Path.Combine(Path.GetTempPath(), "pm-ingest-api-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var peopleSrc = Path.Combine(temp, "people");
            var scenRoot = Path.Combine(temp, "scenarios", "api-scen");
            Directory.CreateDirectory(scenRoot);
            Directory.Move(peopleSrc, Path.Combine(scenRoot, "people"));

            const string marker = "IngestFromExtractorOutputAsyncMarker";
            var json =
                $$"""{"memoryIntents":[{"entityKey":"raha","knowledgeType":"occupation","attribute":"","value":"{{marker}}","confidence":0.95}]}""";

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new QueueLlm());
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var ingest = await runner.IngestFromExtractorOutputAsync(temp, "api-scen", json).ConfigureAwait(false);

            Assert.IsTrue(ingest.ParseSuccess, ingest.Summary);
            Assert.IsTrue(ingest.WroteAnyFile, ingest.Summary);
            var scopedProfile = Path.Combine(temp, "scenarios", "api-scen", "people", "raha", "profile.md");
            Assert.IsTrue(File.Exists(scopedProfile), "Expected scoped profile path.");
            var profile = await File.ReadAllTextAsync(scopedProfile).ConfigureAwait(false);
            StringAssert.Contains(profile, marker);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // best-effort cleanup on CI
            }
        }
    }

    /// <summary>Playground / models often emit a bare intent array; ingest must still write.</summary>
    [TestMethod]
    public async Task IngestFromExtractorOutputAsync_AcceptsRootArrayJson()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        Assert.IsTrue(Directory.Exists(src), $"Missing sample at {src}");

        var temp = Path.Combine(Path.GetTempPath(), "pm-ingest-array-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var peopleSrc = Path.Combine(temp, "people");
            var scenRoot = Path.Combine(temp, "scenarios", "array-scen");
            Directory.CreateDirectory(scenRoot);
            Directory.Move(peopleSrc, Path.Combine(scenRoot, "people"));

            const string marker = "RootArrayIngestMarker";
            var json =
                $$"""
                  [
                    {"entityKey":"match/people/raha/","knowledgeType":"occupation","attribute":"","value":"{{marker}}","confidence":0.95}
                  ]
                  """;

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new QueueLlm());
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var ingest = await runner.IngestFromExtractorOutputAsync(temp, "array-scen", json).ConfigureAwait(false);

            Assert.IsTrue(ingest.ParseSuccess, ingest.Summary);
            Assert.IsTrue(ingest.WroteAnyFile, ingest.Summary);
            var scopedProfile = Path.Combine(temp, "scenarios", "array-scen", "people", "raha", "profile.md");
            Assert.IsTrue(File.Exists(scopedProfile), "Expected scoped profile path.");
            var profile = await File.ReadAllTextAsync(scopedProfile).ConfigureAwait(false);
            StringAssert.Contains(profile, marker);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // best-effort cleanup on CI
            }
        }
    }

    [TestMethod]
    public async Task IngestFromExtractorOutputAsync_AcceptsActionIntentsEnvelope()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        Assert.IsTrue(Directory.Exists(src), $"Missing sample at {src}");

        var temp = Path.Combine(Path.GetTempPath(), "pm-ingest-actions-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var peopleSrc = Path.Combine(temp, "people");
            var scenRoot = Path.Combine(temp, "scenarios", "action-scen");
            Directory.CreateDirectory(scenRoot);
            Directory.Move(peopleSrc, Path.Combine(scenRoot, "people"));

            const string marker = "ActionIntentIngestMarker";
            var json =
                $$"""
                  {
                    "schemaVersion":"1.0",
                    "scenarioId":"action-scen",
                    "actionIntents":[
                      {
                        "intentType":"memory.persist",
                        "payload":{
                          "memoryIntents":[
                            {"entityKey":"match/people/raha/","knowledgeType":"occupation","attribute":"","value":"{{marker}}","confidence":0.95}
                          ]
                        }
                      }
                    ]
                  }
                  """;

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new QueueLlm());
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var ingest = await runner.IngestFromExtractorOutputAsync(temp, "action-scen", json).ConfigureAwait(false);

            Assert.IsTrue(ingest.ParseSuccess, ingest.Summary);
            Assert.IsTrue(ingest.WroteAnyFile, ingest.Summary);
            var scopedProfile = Path.Combine(temp, "scenarios", "action-scen", "people", "raha", "profile.md");
            Assert.IsTrue(File.Exists(scopedProfile), "Expected scoped profile path.");
            var profile = await File.ReadAllTextAsync(scopedProfile).ConfigureAwait(false);
            StringAssert.Contains(profile, marker);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // best-effort cleanup on CI
            }
        }
    }

    [TestMethod]
    public async Task QueryOnly_UsesSingleLlmRoundTrip()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-orchestrator-q-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            var llm = new QueueLlm();
            llm.Responses.Enqueue("Answer from test double.");

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "What is Raha's occupation?",
                Mode = ProjectMemoryPipelineMode.QueryOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("Answer from test double.", result.FinalText);
            Assert.AreEqual(0, llm.Responses.Count);
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
    public async Task IngestOnly_PathStyleEntityKeys_ResolveAndWriteBothProfiles()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-orchestrator-keys-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            var llm = new QueueLlm();
            llm.Responses.Enqueue(
                """
                {
                  "memoryIntents":[
                    {"entityKey":"match/people/Raha","knowledgeType":"profile_fact","attribute":"age","value":"45","confidence":0.9},
                    {"entityKey":"match/people/Ryan","knowledgeType":"profile_fact","attribute":"age","value":"5.5","confidence":0.9}
                  ]
                }
                """);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "Raha is 45 and Ryan is 5.5.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);
            var raha = await File.ReadAllTextAsync(Path.Combine(temp, "people", "raha", "profile.md")).ConfigureAwait(false);
            var ryan = await File.ReadAllTextAsync(Path.Combine(temp, "people", "ryan", "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(raha, "Age: 45");
            StringAssert.Contains(ryan, "Age: 5.5");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task IngestOnly_PartialRouteMiss_StillWritesRoutableIntents()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-orchestrator-partial-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            var llm = new QueueLlm();
            llm.Responses.Enqueue(
                """
                {
                  "memoryIntents":[
                    {"entityKey":"match/people/raha/","knowledgeType":"profile_fact","attribute":"age","value":"45","confidence":0.99},
                    {"entityKey":"match/people/raha/","knowledgeType":"profile_fact","attribute":"unknown_attr","value":"x","confidence":0.99}
                  ]
                }
                """);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "Raha is 45.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);
            var profile = await File.ReadAllTextAsync(Path.Combine(temp, "people", "raha", "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(profile, "Age: 45");
            var routeStep = result.Steps.FirstOrDefault(s => s.Name == "route");
            Assert.IsNotNull(routeStep);
            Assert.IsTrue(routeStep!.Detail?.Contains("Skipped", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task IngestOnly_ExactPromptShape_WritesRahaAndRyanProfiles()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-orchestrator-exact-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            var llm = new QueueLlm();
            llm.Responses.Enqueue(
                """
                {
                  "memoryIntents": [
                    {
                      "entityKey": "match/people/raha/",
                      "knowledgeType": "profile_fact",
                      "attribute": "age",
                      "value": "45",
                      "confidence": 0.99
                    },
                    {
                      "entityKey": "match/people/ryan/",
                      "knowledgeType": "profile_fact",
                      "attribute": "age",
                      "value": "5.5",
                      "confidence": 0.99
                    },
                    {
                      "entityKey": "match/people/ryan/",
                      "knowledgeType": "family_role",
                      "attribute": "child_of",
                      "value": "raha",
                      "confidence": 0.99
                    },
                    {
                      "entityKey": "match/people/raha/",
                      "knowledgeType": "profile_fact",
                      "attribute": "ownership",
                      "value": "Tesla Model Y",
                      "confidence": 0.99
                    }
                  ]
                }
                """);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "Raha is 45 and his son is 5 and half years old his son name is Ryan. Raha also have a Tesla Model Y",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);
            var rahaProfile = await File.ReadAllTextAsync(Path.Combine(temp, "people", "raha", "profile.md")).ConfigureAwait(false);
            var ryanProfile = await File.ReadAllTextAsync(Path.Combine(temp, "people", "ryan", "profile.md")).ConfigureAwait(false);

            StringAssert.Contains(rahaProfile, "Age: 45");
            StringAssert.Contains(rahaProfile, "Ownership: Tesla Model Y");
            StringAssert.Contains(ryanProfile, "Age: 5.5");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task IngestFromExtractor_NewEntity_CreatesAllPersonDocumentsAndProfile()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-orchestrator-new-ent-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new QueueLlm());
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var json =
                """{"memoryIntents":[{"entityKey":"Melody","knowledgeType":"profile_fact","attribute":"age","value":"47","confidence":1}]}""";
            var ingest = await runner.IngestFromExtractorOutputAsync(temp, null, json).ConfigureAwait(false);
            Assert.IsTrue(ingest.ParseSuccess, ingest.Summary);
            Assert.IsTrue(ingest.WroteAnyFile, ingest.Summary);

            var baseDir = Path.Combine(temp, "people", "melody");
            Assert.IsTrue(File.Exists(Path.Combine(baseDir, "entity.yaml")));
            Assert.IsTrue(File.Exists(Path.Combine(baseDir, "profile.md")));
            Assert.IsTrue(File.Exists(Path.Combine(baseDir, "relationships.md")));
            Assert.IsTrue(File.Exists(Path.Combine(baseDir, "skills.md")));
            Assert.IsTrue(File.Exists(Path.Combine(baseDir, "timeline.md")));

            var profile = await File.ReadAllTextAsync(Path.Combine(baseDir, "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(profile, "Age: 47");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task IngestOnly_LastName_IsWrittenToBasicInfo()
    {
        // Regression guard: the Mohebbi-style last_name intent used to drop as "route_miss" because
        // routing only knew about profile_fact/name. Now it should land in Basic Info.
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-lastname-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            var llm = new QueueLlm();
            llm.Responses.Enqueue(
                """
                {
                  "memoryIntents": [
                    {"entityKey":"raha","knowledgeType":"profile_fact","attribute":"name","value":"Raha","confidence":1},
                    {"entityKey":"raha","knowledgeType":"profile_fact","attribute":"last_name","value":"Mohebbi","confidence":1}
                  ]
                }
                """);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "My name is Raha. My last name is Mohebbi.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);
            var profile = await File.ReadAllTextAsync(Path.Combine(temp, "people", "raha", "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(profile, "Mohebbi");
            // Projection renders attribute labels with underscores flipped to spaces and each word
            // title-cased; "last_name" → "Last name:" (whole label lowercased except first letter).
            StringAssert.Contains(profile, "Last name: Mohebbi");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task IngestOnly_Education_IsWrittenToBasicInfo()
    {
        // Education is a stable person profile fact; it should not be promoted through the generic inbox.
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-education-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var inboxDir = Path.Combine(temp, ".agctor", "runtime", "generic-inbox");
            if (Directory.Exists(inboxDir)) Directory.Delete(inboxDir, recursive: true);

            var llm = new QueueLlm();
            llm.Responses.Enqueue(
                """
                {
                  "memoryIntents": [
                    {"entityKey":"raha","knowledgeType":"education","attribute":"","value":"Computer Science degree from the university of Salford in the UK","confidence":1}
                  ]
                }
                """);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "Raha has a degree in Computer Science from the university of Salford in the UK",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);
            var profile = await File.ReadAllTextAsync(Path.Combine(temp, "people", "raha", "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(profile, "Education: Computer Science degree from the university of Salford in the UK");

            var pending = Path.Combine(temp, ".agctor", "runtime", "generic-inbox", "pending.yaml");
            if (File.Exists(pending))
            {
                var pendingText = await File.ReadAllTextAsync(pending).ConfigureAwait(false);
                Assert.IsFalse(pendingText.Contains("Computer Science", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task IngestOnly_FamilyRole_Bootstraps_Referenced_Person_Folder()
    {
        // "I have a son called Ryan" should materialize people/ryan even when the only Ryan-facing
        // intent is a family_role edge whose value is "Ryan" — previously this was dropped.
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-family-boot-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            // Remove any pre-existing ryan folder so we're testing bootstrap, not reuse.
            var ryanDir = Path.Combine(temp, "people", "ryan");
            if (Directory.Exists(ryanDir)) Directory.Delete(ryanDir, true);

            var llm = new QueueLlm();
            llm.Responses.Enqueue(
                """
                {
                  "memoryIntents": [
                    {"entityKey":"raha","knowledgeType":"family_role","attribute":"son","value":"Ryan","confidence":1}
                  ]
                }
                """);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "I have a son called Ryan.",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);
            Assert.IsTrue(File.Exists(Path.Combine(temp, "people", "ryan", "entity.yaml")), "ryan entity.yaml should exist after family_role value bootstrap");
            var ryanYaml = await File.ReadAllTextAsync(Path.Combine(temp, "people", "ryan", "entity.yaml")).ConfigureAwait(false);
            StringAssert.Contains(ryanYaml, "displayName: Ryan");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task IngestOnly_UserPrompt_CapturesLastName_And_BootstrapsReferencedSon()
    {
        // Full regression from the playground run: "My name is Raha. My last name is Mohebbi.
        // I have a son called Ryan who was born on 27th of October."
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-userprompt-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var ryanDir = Path.Combine(temp, "people", "ryan");
            if (Directory.Exists(ryanDir)) Directory.Delete(ryanDir, true);

            var llm = new QueueLlm();
            // Extractor emits: raha name + raha last_name + family edge + ryan's spoken name + ryan DOB.
            llm.Responses.Enqueue(
                """
                {
                  "memoryIntents": [
                    {"entityKey":"raha","knowledgeType":"profile_fact","attribute":"name","value":"Raha","confidence":1},
                    {"entityKey":"raha","knowledgeType":"profile_fact","attribute":"last_name","value":"Mohebbi","confidence":1},
                    {"entityKey":"raha","knowledgeType":"family_role","attribute":"son","value":"Ryan","confidence":1},
                    {"entityKey":"ryan","knowledgeType":"profile_fact","attribute":"name","value":"Ryan","confidence":1},
                    {"entityKey":"ryan","knowledgeType":"profile_fact","attribute":"date_of_birth","value":"27th of October","confidence":0.9}
                  ]
                }
                """);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var result = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                UserMessage = "My name is Raha. My last name is Mohebbi. I have a son called Ryan who was born on 27th of October",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);

            Assert.IsTrue(result.Success, result.FinalText);

            var rahaProfile = await File.ReadAllTextAsync(Path.Combine(temp, "people", "raha", "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(rahaProfile, "Mohebbi");

            var ryanEntity = Path.Combine(temp, "people", "ryan", "entity.yaml");
            Assert.IsTrue(File.Exists(ryanEntity), "ryan entity.yaml should have been bootstrapped");
            var ryanProfile = await File.ReadAllTextAsync(Path.Combine(temp, "people", "ryan", "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(ryanProfile, "27th of October");

            // Family edge should land on both sides with the relation type preserved
            // ("- child: ryan" on Raha, "- parent: raha" on Ryan via the auto-inverse).
            var rahaRels = await File.ReadAllTextAsync(Path.Combine(temp, "people", "raha", "relationships.md")).ConfigureAwait(false);
            var ryanRels = await File.ReadAllTextAsync(Path.Combine(temp, "people", "ryan", "relationships.md")).ConfigureAwait(false);
            StringAssert.Contains(rahaRels, "- child: ryan");
            StringAssert.Contains(ryanRels, "- parent: raha");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task IngestFromExtractor_ScenarioScoped_NewEntity_WritesUnderScenariosFolder()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-orchestrator-scen-new-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);
            var scenRoot = Path.Combine(temp, "scenarios", "people_1");
            Directory.CreateDirectory(scenRoot);
            var peopleSrc = Path.Combine(temp, "people");
            Directory.Move(peopleSrc, Path.Combine(scenRoot, "people"));

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => new QueueLlm());
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var runner = services.BuildServiceProvider().GetRequiredService<IProjectMemoryPipelineRunner>();

            var json =
                """{"memoryIntents":[{"entityKey":"Melody","knowledgeType":"profile_fact","attribute":"age","value":"47","confidence":1}]}""";
            var ingest = await runner.IngestFromExtractorOutputAsync(temp, "people_1", json).ConfigureAwait(false);
            Assert.IsTrue(ingest.ParseSuccess, ingest.Summary);
            Assert.IsTrue(ingest.WroteAnyFile, ingest.Summary);

            var baseDir = Path.Combine(temp, "scenarios", "people_1", "people", "melody");
            Assert.IsTrue(Directory.Exists(baseDir));
            Assert.IsTrue(File.Exists(Path.Combine(baseDir, "profile.md")));
            var profile = await File.ReadAllTextAsync(Path.Combine(baseDir, "profile.md")).ConfigureAwait(false);
            StringAssert.Contains(profile, "Age: 47");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }
}
