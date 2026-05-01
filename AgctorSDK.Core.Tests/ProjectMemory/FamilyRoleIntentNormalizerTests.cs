using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Processing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class FamilyRoleIntentNormalizerTests
{
    private static EntityRecord Person(string key, string display, params string[] aliases) =>
        new()
        {
            EntityKey = key,
            EntityType = "person",
            RootPath = "/x/people/" + key,
            Metadata = new EntityMetadata
            {
                DisplayName = display,
                Aliases = aliases.Length > 0 ? new List<string>(aliases) : null
            },
            DocumentRelativePaths = new List<string>()
        };

    [TestMethod]
    public void ExtractUserMessageCorpus_PrefersSectionAfterMarker()
    {
        var raw = "System...\n---\nLatest user message:\nRaha met Melody.";
        var c = FamilyRoleIntentNormalizer.ExtractUserMessageCorpus(raw);
        StringAssert.Contains(c, "Raha met Melody");
        Assert.IsFalse(c.Contains("System"));
    }

    [TestMethod]
    public void Apply_ParentSynonym_BecomesChildOnParentEntity()
    {
        var discovered = new List<EntityRecord> { Person("melody", "Melody"), Person("raha", "Raha") };
        var intents = new List<MemoryIntent>
        {
            new()
            {
                EntityKey = "raha",
                KnowledgeType = "family_role",
                Attribute = "mother",
                Value = "Melody",
                Confidence = 0.9
            }
        };
        var notes = new List<string>();
        var corpus = "Latest user message:\nRaha's mother is Melody.";

        FamilyRoleIntentNormalizer.Apply(intents, discovered, corpus, notes);

        // Parent→child canonicalization now also emits the inverse (child→parent).
        Assert.AreEqual(2, intents.Count);
        Assert.IsTrue(intents.Exists(m =>
            m.EntityKey.Equals("melody", System.StringComparison.OrdinalIgnoreCase)
            && m.Attribute!.Equals("child", System.StringComparison.OrdinalIgnoreCase)
            && m.Value.Equals("raha", System.StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(intents.Exists(m =>
            m.EntityKey.Equals("raha", System.StringComparison.OrdinalIgnoreCase)
            && m.Attribute!.Equals("parent", System.StringComparison.OrdinalIgnoreCase)
            && m.Value.Equals("melody", System.StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Apply_FuzzySlug_WhenCorpusContainsDisplayName()
    {
        var discovered = new List<EntityRecord> { Person("raha", "Raha"), Person("melody", "Melody") };
        var intents = new List<MemoryIntent>
        {
            new()
            {
                EntityKey = "melody",
                KnowledgeType = "family_role",
                Attribute = "child",
                Value = "rafa",
                Confidence = 0.85
            }
        };
        var notes = new List<string>();
        var corpus = "Latest user message:\nPlease add Raha under Melody as a child.";

        FamilyRoleIntentNormalizer.Apply(intents, discovered, corpus, notes);

        // Child edge + auto-inverse parent edge.
        Assert.AreEqual(2, intents.Count);
        Assert.IsTrue(intents.Exists(m =>
            m.EntityKey.Equals("melody", System.StringComparison.OrdinalIgnoreCase)
            && m.Attribute!.Equals("child", System.StringComparison.OrdinalIgnoreCase)
            && m.Value.Equals("raha", System.StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(notes.Exists(n => n.Contains("fuzzy-matched", System.StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Apply_NoFuzzy_WhenCorpusLacksCorroboratingName()
    {
        var discovered = new List<EntityRecord> { Person("raha", "Raha"), Person("melody", "Melody") };
        var intents = new List<MemoryIntent>
        {
            new()
            {
                EntityKey = "melody",
                KnowledgeType = "family_role",
                Attribute = "child",
                Value = "rafa",
                Confidence = 0.85
            }
        };
        var notes = new List<string>();
        var corpus = "Latest user message:\nAdd rafa under Melody.";

        FamilyRoleIntentNormalizer.Apply(intents, discovered, corpus, notes);

        Assert.AreEqual(0, intents.Count);
        StringAssert.Contains(string.Join("; ", notes), "unresolved value");
    }

    [TestMethod]
    public void Apply_Sibling_AddsInverseEdge()
    {
        var discovered = new List<EntityRecord> { Person("raha", "Raha"), Person("melody", "Melody") };
        var intents = new List<MemoryIntent>
        {
            new()
            {
                EntityKey = "raha",
                KnowledgeType = "family_role",
                Attribute = "sibling",
                Value = "melody",
                Confidence = 0.9
            }
        };
        var notes = new List<string>();
        var corpus = "Latest user message:\nRaha and Melody are siblings.";

        FamilyRoleIntentNormalizer.Apply(intents, discovered, corpus, notes);

        Assert.AreEqual(2, intents.Count);
        Assert.IsTrue(intents.Exists(m =>
            m.EntityKey.Equals("melody", System.StringComparison.OrdinalIgnoreCase)
            && m.Value.Equals("raha", System.StringComparison.OrdinalIgnoreCase)
            && m.Attribute?.Equals("sibling", System.StringComparison.OrdinalIgnoreCase) == true));
    }

    [TestMethod]
    public void Apply_NewEntitySlug_PreservedWhenNoConflictingNearNeighbor()
    {
        var discovered = new List<EntityRecord> { Person("raha", "Raha") };
        var intents = new List<MemoryIntent>
        {
            new()
            {
                EntityKey = "melody",
                KnowledgeType = "family_role",
                Attribute = "child",
                Value = "raha",
                Confidence = 0.9
            }
        };
        var notes = new List<string>();
        var corpus = "Latest user message:\nCreate Melody as Raha's parent in the graph.";

        FamilyRoleIntentNormalizer.Apply(intents, discovered, corpus, notes);

        // Child edge kept + inverse parent edge auto-added.
        Assert.AreEqual(2, intents.Count);
        Assert.IsTrue(intents.Exists(m =>
            string.Equals(m.EntityKey, "melody", System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Attribute, "child", System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Value, "raha", System.StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(intents.Exists(m =>
            string.Equals(m.EntityKey, "raha", System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Attribute, "parent", System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Value, "melody", System.StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Apply_SonEdge_AddsInverseParentEdge()
    {
        // "raha son ryan" is normalized to (raha, child, ryan); we expect an auto-generated
        // (ryan, parent, raha) so Ryan's relationships.md also records the tie.
        var discovered = new List<EntityRecord> { Person("raha", "Raha") };
        var intents = new List<MemoryIntent>
        {
            new()
            {
                EntityKey = "raha",
                KnowledgeType = "family_role",
                Attribute = "son",
                Value = "ryan",
                Confidence = 0.9
            }
        };
        var notes = new List<string>();

        FamilyRoleIntentNormalizer.Apply(intents, discovered, "Latest user message:\nI have a son called Ryan.", notes);

        Assert.AreEqual(2, intents.Count, string.Join("; ", notes));
        Assert.IsTrue(intents.Exists(m =>
            m.EntityKey.Equals("raha", System.StringComparison.OrdinalIgnoreCase)
            && m.Attribute!.Equals("child", System.StringComparison.OrdinalIgnoreCase)
            && m.Value.Equals("ryan", System.StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(intents.Exists(m =>
            m.EntityKey.Equals("ryan", System.StringComparison.OrdinalIgnoreCase)
            && m.Attribute!.Equals("parent", System.StringComparison.OrdinalIgnoreCase)
            && m.Value.Equals("raha", System.StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Apply_NonFamilyIntent_Unchanged()
    {
        var discovered = new List<EntityRecord> { Person("raha", "Raha") };
        var intents = new List<MemoryIntent>
        {
            new()
            {
                EntityKey = "typo-occupation",
                KnowledgeType = "occupation",
                Attribute = "",
                Value = "Engineer",
                Confidence = 0.9
            }
        };
        var notes = new List<string>();

        FamilyRoleIntentNormalizer.Apply(intents, discovered, "Latest user message:\nRaha is an engineer.", notes);

        Assert.AreEqual(1, intents.Count);
        Assert.AreEqual("typo-occupation", intents[0].EntityKey);
    }
}
