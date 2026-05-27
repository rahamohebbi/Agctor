using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Visual;

[TestClass]
public sealed class VisualMessageIdentityHintsTests
{
    [TestMethod]
    public void This_is_me_Raha_links_subject()
    {
        var record = new VisualAssetRecord { AssetId = "a1", State = VisualAssetStates.Uploaded };
        var applied = VisualMessageIdentityHints.TryApplyToRecord(
            record,
            "this is me Raha",
            focusEntityKey: null,
            projectRoot: null,
            scenarioId: null);

        applied.Should().BeTrue();
        record.Subjects.Should().ContainSingle(s => s.EntityKey == "raha");
        record.State.Should().Be(VisualAssetStates.ReadyForExtract);
    }
}
