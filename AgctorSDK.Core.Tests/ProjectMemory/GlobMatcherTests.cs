using AgctorSDK.Core.ProjectMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class GlobMatcherTests
{
    [TestMethod]
    public void Star_MatchesSingleSegment()
    {
        Assert.IsTrue(GlobMatcher.IsMatch("people/raha/profile.md", "people/*/profile.md"));
        Assert.IsFalse(GlobMatcher.IsMatch("people/raha/extra/profile.md", "people/*/profile.md"));
    }

    [TestMethod]
    public void StarStar_MatchesNested()
    {
        Assert.IsTrue(GlobMatcher.IsMatch("schemas/people/x.yaml", "schemas/**"));
    }
}
