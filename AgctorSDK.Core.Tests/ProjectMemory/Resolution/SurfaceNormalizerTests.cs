using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class SurfaceNormalizerTests
{
    [TestMethod]
    public void Lowercases_Trims_And_Collapses_Whitespace()
    {
        Assert.AreEqual("raha mohebbi", SurfaceNormalizer.Normalize("  Raha   Mohebbi  "));
    }

    [TestMethod]
    public void Strips_Diacritics()
    {
        Assert.AreEqual("raha", SurfaceNormalizer.Normalize("Rähä"));
        Assert.AreEqual("jose", SurfaceNormalizer.Normalize("José"));
    }

    [TestMethod]
    public void Null_Or_Empty_Returns_Empty()
    {
        Assert.AreEqual("", SurfaceNormalizer.Normalize(null));
        Assert.AreEqual("", SurfaceNormalizer.Normalize("   "));
    }
}
