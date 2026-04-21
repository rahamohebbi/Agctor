using AgctorSDK.Core.ProjectMemory.Resolution.Observability;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class ResolutionMetricsTests
{
    [TestMethod]
    public void Increments_Accumulate()
    {
        var m = new ResolutionMetrics();
        m.Increment("a");
        m.Increment("a", 5);
        m.Increment("b");
        Assert.AreEqual(6, m.Get("a"));
        Assert.AreEqual(1, m.Get("b"));
    }

    [TestMethod]
    public void Snapshot_Returns_Copy()
    {
        var m = new ResolutionMetrics();
        m.Increment("x", 2);
        var snap = m.Snapshot();
        m.Increment("x", 3);
        Assert.AreEqual(2, snap["x"]); // snapshot was point-in-time
        Assert.AreEqual(5, m.Get("x"));
    }
}
