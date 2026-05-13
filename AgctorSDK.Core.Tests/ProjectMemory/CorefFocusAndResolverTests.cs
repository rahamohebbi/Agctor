using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Coref;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class CorefFocusAndResolverTests
{
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static void CopyDir(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            var d = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
            File.Copy(file, target, overwrite: true);
        }
    }

    [TestMethod]
    public async Task FocusStore_SaveAndLoad_PersistsPerScenarioOnDisk()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pm-focus-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, ".agctor", "runtime"));
            var store = new ConversationFocusStore();

            var focus = new ConversationFocus
            {
                EntityKey = "raha",
                DisplayName = "Raha Mohebbi",
                UpdatedBySessionId = "session-A",
                Source = "extracted"
            };
            await store.SaveAsync(temp, "person_1", focus).ConfigureAwait(false);

            // A brand-new store instance simulates a new browser session / Host restart loading from disk.
            var freshStore = new ConversationFocusStore();
            var loaded = await freshStore.LoadAsync(temp, "person_1").ConfigureAwait(false);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("raha", loaded!.EntityKey);
            Assert.AreEqual("Raha Mohebbi", loaded.DisplayName);

            // Different scenario must not see another scenario's focus.
            var other = await freshStore.LoadAsync(temp, "person_2").ConfigureAwait(false);
            Assert.IsNull(other, "Focus is scoped per scenario; other scenarios start with no focus.");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task LlmCorefResolver_Rewrites_PronounishMessage_UsingFocus()
    {
        var llm = new ScriptedLlm("{\"changed\":true,\"rewrittenMessage\":\"Raha likes to play basketball as well\",\"activeSubject\":\"raha\"}");
        var resolver = new LlmConversationCoreferenceResolver(llm);
        var resolution = await resolver.ResolveAsync(new CoreferenceRequest
        {
            UserMessage = "He likes to play basketball as well",
            ConversationPrefix = "User: My name is Raha and I am currently living in California.\n",
            CurrentFocus = new ConversationFocus { EntityKey = "raha", DisplayName = "Raha" },
            KnownEntities = new[]
            {
                new KnownEntity { EntityKey = "raha", DisplayName = "Raha" }
            }
        }).ConfigureAwait(false);

        Assert.IsTrue(resolution.Changed);
        StringAssert.Contains(resolution.RewrittenMessage, "Raha");
        Assert.AreEqual("raha", resolution.ActiveSubjectEntityKey);
    }

    [TestMethod]
    public async Task LlmCorefResolver_DoesNotCallLlm_WhenMessageNamesEntityExplicitly()
    {
        var llm = new ScriptedLlm(throwIfCalled: true);
        var resolver = new LlmConversationCoreferenceResolver(llm);
        var resolution = await resolver.ResolveAsync(new CoreferenceRequest
        {
            UserMessage = "Raha lives in Tehran now",
            ConversationPrefix = "User: hi\n",
            // Simulate stale focus from a prior run/session: explicit naming in this turn must override it.
            CurrentFocus = new ConversationFocus { EntityKey = "person1", DisplayName = "Person 1" },
            KnownEntities = new[]
            {
                new KnownEntity { EntityKey = "raha", DisplayName = "Raha" }
            }
        }).ConfigureAwait(false);

        Assert.IsFalse(resolution.Changed);
        Assert.AreEqual("raha", resolution.ActiveSubjectEntityKey);
        Assert.AreEqual(0, llm.CallCount);
    }

    [TestMethod]
    public async Task LlmCorefResolver_RejectsResponses_WithUnknownSlug()
    {
        // The model tries to invent a brand-new slug ("ryan") that is not in the whitelist.
        var llm = new ScriptedLlm("{\"changed\":true,\"rewrittenMessage\":\"ryan likes basketball\",\"activeSubject\":\"ryan\"}");
        var resolver = new LlmConversationCoreferenceResolver(llm);
        var resolution = await resolver.ResolveAsync(new CoreferenceRequest
        {
            UserMessage = "He likes basketball",
            ConversationPrefix = "User: prior\n",
            CurrentFocus = new ConversationFocus { EntityKey = "raha", DisplayName = "Raha" },
            KnownEntities = new[] { new KnownEntity { EntityKey = "raha", DisplayName = "Raha" } }
        }).ConfigureAwait(false);

        Assert.IsFalse(resolution.Changed,
            "Resolver must not accept rewrites that mention slugs outside the whitelist.");
        Assert.AreEqual("raha", resolution.ActiveSubjectEntityKey,
            "ActiveSubject should fall back to the previously known focus.");
    }

    [TestMethod]
    public async Task LlmCorefResolver_DoesNotCallLlm_WhenMessageTooLong()
    {
        var llm = new ScriptedLlm(throwIfCalled: true);
        var resolver = new LlmConversationCoreferenceResolver(llm);
        var longMessage = new string('a', 240);
        var resolution = await resolver.ResolveAsync(new CoreferenceRequest
        {
            UserMessage = longMessage,
            ConversationPrefix = "x",
            KnownEntities = new[] { new KnownEntity { EntityKey = "raha", DisplayName = "Raha" } }
        }).ConfigureAwait(false);

        Assert.IsFalse(resolution.Changed);
        Assert.AreEqual(0, llm.CallCount);
    }

    [TestMethod]
    public async Task PipelineRunner_SetsFocusFromExtract_AndPersistsAcrossSessions()
    {
        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-focus-pipeline-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            // Use heuristic resolver to keep this test deterministic; we are validating focus persistence.
            var llmExtract = new ScriptedLlm(
                """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"profile_fact","attribute":"name","value":"Raha","confidence":1}]}""");

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llmExtract);
            services.AddSingleton<IProjectMemoryPipelineRunner, ProjectMemoryPipelineRunner>();
            var sp = services.BuildServiceProvider();
            var runner = sp.GetRequiredService<IProjectMemoryPipelineRunner>();

            var first = await runner.RunAsync(new ProjectMemoryPipelineRequest
            {
                ProjectRoot = temp,
                ScenarioId = "person_1",
                SessionId = "session-A",
                UserMessage = "My name is Raha and I am currently living in California, Irvine city",
                Mode = ProjectMemoryPipelineMode.IngestOnly
            }).ConfigureAwait(false);
            Assert.IsTrue(first.Success, first.FinalText);

            // Focus file must exist with raha as the active subject so a new browser session inherits it.
            var focusFile = ConversationFocusPaths.FocusFile(temp, "person_1");
            Assert.IsTrue(File.Exists(focusFile), "Focus file should be written after extraction.");
            var focusYaml = await File.ReadAllTextAsync(focusFile).ConfigureAwait(false);
            StringAssert.Contains(focusYaml, "entityKey: raha");

            // A fresh focus-store load (independent of pipeline) must see the saved focus.
            var loaded = await new ConversationFocusStore().LoadAsync(temp, "person_1").ConfigureAwait(false);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("raha", loaded!.EntityKey);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task Coordinator_RewritesPronoun_AndPersistsFocus_AcrossSessions()
    {
        // First the resolver answers (rewrite "He" → "Raha"), then the extractor answers
        // with a name intent. Pipeline ordering: coref LLM call -> extractor LLM call.
        var rewriteJson = "{\"changed\":true,\"rewrittenMessage\":\"Raha likes basketball\",\"activeSubject\":\"raha\"}";
        var extractJson =
            "{\"memoryIntents\":[{\"entityKey\":\"raha\",\"knowledgeType\":\"preference\",\"attribute\":\"sport\",\"value\":\"basketball\",\"confidence\":1}]}";
        var llm = new ScriptedLlm(rewriteJson, extractJson);

        var src = Path.Combine(RepoRoot(), "samples", "people-project");
        var temp = Path.Combine(Path.GetTempPath(), "pm-coord-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDir(src, temp);

            // Seed focus on raha so the resolver has a target for "He".
            var seedFocus = new ConversationFocus
            {
                EntityKey = "raha",
                DisplayName = "Raha",
                UpdatedBySessionId = "session-seed",
                Source = "extracted"
            };
            await new ConversationFocusStore().SaveAsync(temp, "person_1", seedFocus).ConfigureAwait(false);

            var services = new ServiceCollection();
            services.AddAgctorProjectMemory();
            services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
            var sp = services.BuildServiceProvider();
            var coordinator = sp.GetRequiredService<IProjectMemoryCoreferenceCoordinator>();

            var result = await coordinator.PreprocessAsync(
                temp,
                scenarioId: "person_1",
                userMessage: "He likes basketball",
                conversationPrefix: "User: My name is Raha.\n",
                cancellationToken: default).ConfigureAwait(false);

            Assert.IsTrue(result.Changed, "Coordinator should hand back the resolver's rewritten message.");
            Assert.AreEqual("Raha likes basketball", result.ResolvedUserMessage);
            Assert.AreEqual("raha", result.ActiveSubjectKey);

            // Persist focus from the simulated extractor output and confirm a fresh store sees raha.
            await coordinator.PersistFocusFromExtractAsync(
                temp,
                scenarioId: "person_1",
                rawExtractorLlmText: extractJson,
                activeSubjectFromPreprocess: result.ActiveSubjectKey,
                knownEntities: result.KnownEntities,
                sessionId: "session-test",
                cancellationToken: default).ConfigureAwait(false);

            var reloaded = await new ConversationFocusStore().LoadAsync(temp, "person_1").ConfigureAwait(false);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual("raha", reloaded!.EntityKey);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task Coordinator_DegradesGracefully_OnInvalidProjectRoot()
    {
        // Coordinator must never throw; even with a bogus project root it returns the original message
        // so the playground SSE flow path can fall back to running extraction unchanged.
        var llm = new ScriptedLlm(throwIfCalled: false);
        var services = new ServiceCollection();
        services.AddAgctorProjectMemory();
        services.AddSingleton<IProjectMemoryLlmClient>(_ => llm);
        var sp = services.BuildServiceProvider();
        var coordinator = sp.GetRequiredService<IProjectMemoryCoreferenceCoordinator>();

        var bogus = Path.Combine(Path.GetTempPath(), "pm-coord-missing-" + Guid.NewGuid().ToString("N"));
        var result = await coordinator.PreprocessAsync(
            bogus,
            scenarioId: "no_scope",
            userMessage: "He likes basketball",
            conversationPrefix: null,
            cancellationToken: default).ConfigureAwait(false);

        Assert.IsFalse(result.Changed);
        Assert.AreEqual("He likes basketball", result.ResolvedUserMessage);
        Assert.IsNull(result.ActiveSubjectKey);
    }

    private sealed class ScriptedLlm : IProjectMemoryLlmClient
    {
        private readonly Queue<string> _responses;
        private readonly bool _throwIfCalled;
        public int CallCount { get; private set; }

        public ScriptedLlm(params string[] responses)
        {
            _responses = new Queue<string>(responses);
            _throwIfCalled = false;
        }

        public ScriptedLlm(bool throwIfCalled)
        {
            _responses = new Queue<string>();
            _throwIfCalled = throwIfCalled;
        }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_throwIfCalled)
                throw new InvalidOperationException("LLM should not have been called for this scenario.");
            return Task.FromResult(_responses.Count == 0 ? "" : _responses.Dequeue());
        }
    }
}
