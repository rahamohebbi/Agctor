using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class AliasMatcherTests
{
    private static SignalContext Ctx(string surface, string display, params string[] aliases) => new()
    {
        Mention = new MentionRef { SurfaceForm = surface },
        CandidateEntityKey = "raha",
        CandidateDisplayName = display,
        CandidateAliases = aliases
    };

    private static ResolutionPolicy Policy() => new ResolutionPolicy();

    [TestMethod]
    public void Exact_Match_Scores_One()
    {
        var s = new AliasMatcher().Score(Ctx("Raha", "Raha"), Policy());
        Assert.IsNotNull(s);
        Assert.AreEqual("aliasMatch", s!.Kind);
        Assert.AreEqual(1.0, s.Score);
        Assert.AreEqual(0.25, s.Weight);
    }

    [TestMethod]
    public void Alias_Match_Counts()
    {
        var s = new AliasMatcher().Score(Ctx("Raha Mohebbi", "Raha", "Raha Mohebbi"), Policy());
        Assert.IsNotNull(s);
        Assert.AreEqual(1.0, s!.Score);
    }

    [TestMethod]
    public void Substring_Match_Gets_Partial_Score()
    {
        var s = new AliasMatcher().Score(Ctx("Raha", "Raha Mohebbi"), Policy());
        Assert.IsNotNull(s);
        Assert.AreEqual(0.6, s!.Score);
    }

    [TestMethod]
    public void No_Match_Returns_Null()
    {
        var s = new AliasMatcher().Score(Ctx("Sarah", "Raha"), Policy());
        Assert.IsNull(s);
    }

    [TestMethod]
    public void Produces_Stable_Fingerprint()
    {
        var a = new AliasMatcher().Score(Ctx("Raha", "Raha"), Policy());
        var b = new AliasMatcher().Score(Ctx("Raha", "Raha"), Policy());
        Assert.AreEqual(a!.InputsFingerprint, b!.InputsFingerprint);
    }
}
