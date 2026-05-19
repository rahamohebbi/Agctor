using AgctorSDK.Core.ProjectMemory.LifeSignals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class PersonLifeSignalsReaderTests
{
    private string _root = "";

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "agctor-life-signals-" + Guid.NewGuid().ToString("N"));
        var people = Path.Combine(_root, "scenarios", "person_3", "people", "ryan");
        Directory.CreateDirectory(people);
        File.WriteAllText(Path.Combine(people, "profile.md"), """
            # Ryan

            ## Basic Info
            - name: Ryan
            - birthday: October 27
            """);
        File.WriteAllText(Path.Combine(people, "timeline.md"), """
            # Ryan Timeline

            ## Observations
            - 2020-01-15 - Had coffee and talked about school.
            """);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Scan_FindsUpcomingBirthday_ForScenarioWorkspace()
    {
        var signals = PersonLifeSignalsReader.Scan(_root, "person_3", new DateTime(2026, 10, 20), birthdayHorizonDays: 14);
        Assert.IsTrue(signals.Any(s => s.Kind == "birthday_upcoming" && s.EntityKey == "ryan"));
    }

    [TestMethod]
    public void Scan_FlagsStaleContact_WhenTimelineIsOld()
    {
        var signals = PersonLifeSignalsReader.Scan(_root, "person_3", new DateTime(2026, 5, 17), staleContactDays: 30);
        Assert.IsTrue(signals.Any(s => s.Kind == "stale_contact" && s.EntityKey == "ryan"));
    }

    [TestMethod]
    public void Scan_FindsUpcomingBirthday_ForCuratorDateOfBirthLine()
    {
        var people = Path.Combine(_root, "scenarios", "person_3", "people", "raha");
        Directory.CreateDirectory(people);
        File.WriteAllText(Path.Combine(people, "profile.md"), """
            # Raha Profile
            ## Basic Info
            Name: Raha
            Date of birth: 22nd May 1980
            """);
        File.WriteAllText(Path.Combine(people, "timeline.md"), """
            # Raha Timeline
            ## Observations
            - 2026-05-01 - Checked in.
            """);

        var signals = PersonLifeSignalsReader.Scan(_root, "person_3", new DateTime(2026, 5, 18), birthdayHorizonDays: 14);
        Assert.IsTrue(signals.Any(s => s.Kind == "birthday_upcoming" && s.EntityKey == "raha" && s.DaysUntil == 4));
    }
}
