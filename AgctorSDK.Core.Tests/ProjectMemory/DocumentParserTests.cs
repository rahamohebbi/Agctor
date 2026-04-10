using AgctorSDK.Core.ProjectMemory.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class DocumentParserTests
{
    [TestMethod]
    public void Parse_SplitsLevel2Headings()
    {
        var md = """
            # Title

            ## A
            line1

            ## B
            x
            """;
        var p = new DocumentParser();
        var d = p.Parse(md);
        Assert.AreEqual(2, d.Sections.Count);
        Assert.AreEqual("A", d.Sections[0].Title);
        Assert.IsTrue(d.Sections[0].Body.Contains("line1"));
        Assert.AreEqual("B", d.Sections[1].Title);
    }

    [TestMethod]
    public void Compose_RoundTrips()
    {
        var c = DocumentParser.Compose("# Hi", new[] { ("One", "a"), ("Two", "b") });
        StringAssert.Contains(c, "## One");
        StringAssert.Contains(c, "## Two");
    }
}
