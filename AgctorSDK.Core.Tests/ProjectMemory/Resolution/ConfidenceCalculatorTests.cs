using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class ConfidenceCalculatorTests
{
    private static ResolutionSignal Sig(string kind, double score, double weight, bool negative = false) => new()
    {
        Kind = kind, Score = score, Weight = weight, IsNegative = negative
    };

    [TestMethod]
    public void Positive_Signals_Sum_Weighted_Scores()
    {
        var policy = new ResolutionPolicy();
        var signals = new List<ResolutionSignal>
        {
            Sig("aliasMatch", 1.0, 0.25),
            Sig("uniqueness", 1.0, 0.20),
            Sig("corefInSession", 0.5, 0.20)
        };
        var c = ConfidenceCalculator.Compute(signals, policy);
        Assert.AreEqual(0.55, c, 1e-9);
    }

    [TestMethod]
    public void Negative_Caps_Below_Hard_Threshold()
    {
        var policy = new ResolutionPolicy();
        var signals = new List<ResolutionSignal>
        {
            Sig("aliasMatch",      1.0, 0.25),
            Sig("uniqueness",      1.0, 0.20),
            Sig("corefInSession",  1.0, 0.20),
            Sig("attrOverlap",     1.0, 0.15),
            Sig("embedding",       1.0, 0.10),
            Sig("graphConsistency",1.0, 0.10),
            Sig("negative",        1.0, 1.0, negative: true)
        };
        var c = ConfidenceCalculator.Compute(signals, policy);
        Assert.IsTrue(c < policy.HardThreshold, $"expected below {policy.HardThreshold}, got {c}");
    }

    [TestMethod]
    public void Independent_Families_Counts_Distinct_Kinds()
    {
        var signals = new List<ResolutionSignal>
        {
            Sig("aliasMatch", 1.0, 0.25),
            Sig("aliasMatch", 0.5, 0.25),
            Sig("uniqueness", 1.0, 0.20)
        };
        Assert.AreEqual(2, ConfidenceCalculator.IndependentPositiveFamilies(signals));
    }
}
