using AgctorSDK.Core.ProjectMemory.Inbox;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class GenericInboxDecisionServiceTests
{
    private string _root = "";

    [TestInitialize]
    public void Init() => _root = Path.Combine(Path.GetTempPath(), "inbox-decide-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task ApplyDecisions_Rejects_Pending_Row()
    {
        var store = new GenericInboxStore();
        var intent = new MemoryIntent
        {
            EntityKey = "raha",
            KnowledgeType = "profile",
            Attribute = "note",
            Value = "likes hiking",
            Confidence = 0.8
        };
        var proposal = new OutOfSchemaFactProposal
        {
            ProposalId = OutOfSchemaProposalFactory.ComputeProposalId(intent),
            EntityKey = intent.EntityKey,
            KnowledgeType = intent.KnowledgeType,
            Attribute = intent.Attribute,
            Value = intent.Value,
            Confidence = intent.Confidence,
            Disposition = OutOfSchemaDisposition.ReviewQueue,
            UserPromptLine = OutOfSchemaProposalFactory.BuildUserPromptLine(intent)
        };
        await store.AppendPendingAsync(_root, "person_3", [proposal]);

        var svc = new GenericInboxDecisionService(store);
        var result = await svc.ApplyDecisionsAsync(
            _root,
            "person_3",
            [new GenericInboxDecision { ProposalId = proposal.ProposalId, Approve = false }]);

        Assert.AreEqual(1, result.Rejected);
        var pending = await store.LoadPendingAsync(_root);
        Assert.AreEqual(0, pending.Count);
    }
}
