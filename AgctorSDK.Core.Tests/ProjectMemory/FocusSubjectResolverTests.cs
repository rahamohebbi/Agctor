using System;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Coref;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class FocusSubjectResolverTests
{
    [TestMethod]
    public async Task HeuristicFocus_PinsEarliestNamedEntity_WhenMultiplePeopleMentioned()
    {
        var resolver = new HeuristicFocusSubjectResolver();
        var result = await resolver.ResolveAsync(new FocusSubjectRequest
        {
            UserMessage = "Ryan is Raha's son. Ryan's imagination is amazing.",
            CurrentFocusEntityKey = "raha",
            KnownEntities = new[]
            {
                new KnownEntity { EntityKey = "raha", DisplayName = "Raha Mohebbi" },
                new KnownEntity { EntityKey = "ryan", DisplayName = "Ryan" }
            }
        }).ConfigureAwait(false);

        Assert.AreEqual("ryan", result.EntityKey);
        Assert.IsTrue(result.ChangedFromCurrent);
    }

    [TestMethod]
    public async Task HeuristicFocus_ExplicitNameOverridesStaleFocus()
    {
        var resolver = new HeuristicFocusSubjectResolver();
        var result = await resolver.ResolveAsync(new FocusSubjectRequest
        {
            UserMessage = "Raha lives in Tehran now",
            CurrentFocusEntityKey = "person1",
            KnownEntities = new[]
            {
                new KnownEntity { EntityKey = "raha", DisplayName = "Raha" }
            }
        }).ConfigureAwait(false);

        Assert.AreEqual("raha", result.EntityKey);
        Assert.IsTrue(result.ChangedFromCurrent);
    }

    [TestMethod]
    public async Task LlmFocus_PicksRyan_WhenMessageIsMainlyAboutRyan()
    {
        var llm = new ScriptedFocusLlm("{\"activeSubject\":\"ryan\",\"reason\":\"grammatical subject\"}");
        var resolver = new FocusSubjectResolver(llm);
        var result = await resolver.ResolveAsync(new FocusSubjectRequest
        {
            UserMessage = "Ryan is Raha's son. Ryan's imagination is amazing.",
            CurrentFocusEntityKey = "raha",
            KnownEntities = new[]
            {
                new KnownEntity { EntityKey = "raha", DisplayName = "Raha Mohebbi" },
                new KnownEntity { EntityKey = "ryan", DisplayName = "Ryan" }
            }
        }).ConfigureAwait(false);

        Assert.AreEqual("ryan", result.EntityKey);
        Assert.AreEqual(1, llm.CallCount);
    }

    [TestMethod]
    public async Task LlmFocus_RejectsUnknownSlug()
    {
        var llm = new ScriptedFocusLlm("{\"activeSubject\":\"unknown\",\"reason\":\"bad\"}");
        var resolver = new FocusSubjectResolver(llm);
        var result = await resolver.ResolveAsync(new FocusSubjectRequest
        {
            UserMessage = "Tell me about Ryan",
            CurrentFocusEntityKey = "raha",
            KnownEntities = new[]
            {
                new KnownEntity { EntityKey = "raha", DisplayName = "Raha" },
                new KnownEntity { EntityKey = "ryan", DisplayName = "Ryan" }
            }
        }).ConfigureAwait(false);

        Assert.AreEqual("raha", result.EntityKey);
    }

    private sealed class ScriptedFocusLlm : AgctorSDK.Core.ProjectMemory.Orchestration.IProjectMemoryLlmClient
    {
        private readonly string _response;
        public int CallCount { get; private set; }

        public ScriptedFocusLlm(string response) => _response = response;

        public Task<string> GenerateAsync(string prompt, System.Threading.CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_response);
        }
    }
}
