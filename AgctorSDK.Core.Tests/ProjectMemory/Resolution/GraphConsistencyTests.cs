using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class GraphConsistencyTests
{
    [TestMethod]
    public void Self_Reference_Yields_Negative_Signal()
    {
        var ctx = new SignalContext
        {
            Mention = new MentionRef { WithinEntityKey = "raha" },
            CandidateEntityKey = "raha"
        };
        var s = new GraphConsistency().Score(ctx, new ResolutionPolicy());
        Assert.IsNotNull(s);
        Assert.IsTrue(s!.IsNegative);
    }

    [TestMethod]
    public void Foreign_Host_Gives_Small_Positive()
    {
        var ctx = new SignalContext
        {
            Mention = new MentionRef { WithinEntityKey = "ryan" },
            CandidateEntityKey = "raha"
        };
        var s = new GraphConsistency().Score(ctx, new ResolutionPolicy());
        Assert.IsNotNull(s);
        Assert.IsFalse(s!.IsNegative);
        Assert.IsTrue(s.Score > 0);
    }
}
