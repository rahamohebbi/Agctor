using System;
using System.IO;
using AgctorSDK.Core.ProjectMemory.Resolution.Persistence;
using AgctorSDK.Core.ProjectMemory.Resolution.Policy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class ResolutionPolicyLoaderTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "res-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, ResolutionPaths.AgctorFolder));
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public void Missing_File_Returns_Defaults_Disabled()
    {
        var p = ResolutionPolicyLoader.Load(_root);
        Assert.IsFalse(p.Enabled);
        Assert.AreEqual(0.90, p.HardThreshold);
        Assert.AreEqual(0.55, p.SoftThreshold);
    }

    [TestMethod]
    public void Overrides_Take_Effect()
    {
        var yaml = @"
enabled: true
hardThreshold: 0.80
softThreshold: 0.40
signalWeights:
  aliasMatch: 0.30
  uniqueness: 0.10
reconciler:
  coalesceWindowMs: 1000
review:
  autoPromote: false
";
        File.WriteAllText(ResolutionPaths.PolicyPath(_root), yaml);

        var p = ResolutionPolicyLoader.Load(_root);
        Assert.IsTrue(p.Enabled);
        Assert.AreEqual(0.80, p.HardThreshold);
        Assert.AreEqual(0.40, p.SoftThreshold);
        Assert.AreEqual(0.30, p.WeightFor("aliasMatch"));
        Assert.AreEqual(0.10, p.WeightFor("uniqueness"));
        Assert.AreEqual(1000, p.Reconciler.CoalesceWindowMs);
        Assert.IsFalse(p.Review.AutoPromote);
    }
}
