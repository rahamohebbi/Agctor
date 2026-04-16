using System.IO;
using AgctorSDK.Core.ProjectMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class PersonaScenarioScopeTests
{
    [TestMethod]
    public void SanitizeFolderSegment_StripsUnsafeChars()
    {
        Assert.AreEqual("people", PersonaScenarioScope.SanitizeFolderSegment("people"));
        Assert.AreEqual("evil", PersonaScenarioScope.SanitizeFolderSegment("../../evil"));
        Assert.AreEqual("_default", PersonaScenarioScope.SanitizeFolderSegment("@@@"));
    }

    [TestMethod]
    public void GetEntityWorkspaceRoot_NoScenario_ReturnsProjectRoot()
    {
        var tmp = Path.GetTempPath();
        var r = Path.GetFullPath(Path.Combine(tmp, "p1"));
        Assert.AreEqual(r, PersonaScenarioScope.GetEntityWorkspaceRoot(r, null));
        Assert.AreEqual(r, PersonaScenarioScope.GetEntityWorkspaceRoot(r, "   "));
    }

    [TestMethod]
    public void GetEntityWorkspaceRoot_WithScenario_AppendsScenariosSegment()
    {
        var tmp = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "scope-" + nameof(GetEntityWorkspaceRoot_WithScenario_AppendsScenariosSegment)));
        var expected = Path.GetFullPath(Path.Combine(tmp, "scenarios", "my-scen"));
        Assert.AreEqual(expected, PersonaScenarioScope.GetEntityWorkspaceRoot(tmp, "my-scen"));
    }
}
