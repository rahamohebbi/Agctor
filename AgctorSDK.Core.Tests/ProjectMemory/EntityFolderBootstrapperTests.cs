using AgctorSDK.Core.ProjectMemory.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class EntityFolderBootstrapperTests
{
    [TestMethod]
    public void SlugFolderSegment_StripsNonAlphanumeric_AndLowercases()
    {
        Assert.AreEqual("melody", EntityFolderBootstrapper.SlugFolderSegment("Melody"));
        Assert.AreEqual("melody", EntityFolderBootstrapper.SlugFolderSegment("match/people/Melody/"));
        Assert.AreEqual("", EntityFolderBootstrapper.SlugFolderSegment("///"));
    }
}
