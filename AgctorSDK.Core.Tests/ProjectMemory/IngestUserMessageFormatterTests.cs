using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public class IngestUserMessageFormatterTests
{
    [TestMethod]
    public void Format_groups_facts_by_person_with_readable_labels()
    {
        var raw = """
                  {
                    "memoryIntents": [
                      {"entityKey":"raha","knowledgeType":"skill","value":"Creates software products","confidence":0.95},
                      {"entityKey":"raha","knowledgeType":"skill","value":"Builds with wood, plastic, and a 3D printer","confidence":0.9}
                    ]
                  }
                  """;

        var ingest = new ProjectMemoryIngestResult
        {
            ParseSuccess = true,
            WroteAnyFile = true,
            UpdatedFiles = new[] { @"C:\proj\scenarios\person_3\people\raha\skills.md" }
        };

        var text = IngestUserMessageFormatter.Format(ingest, raw, @"C:\proj");

        StringAssert.Contains(text, "Saved for **Raha**:");
        StringAssert.Contains(text, "**Skill:** Creates software products");
        StringAssert.Contains(text, "**Skill:** Builds with wood, plastic, and a 3D printer");
        StringAssert.Contains(text, "scenarios/person_3/people/raha/skills.md");
    }

    [TestMethod]
    public void Format_uses_name_intent_for_display_title()
    {
        var raw = """
                  {
                    "memoryIntents": [
                      {"entityKey":"raha","knowledgeType":"profile_fact","attribute":"name","value":"Raha Mohebbi","confidence":1},
                      {"entityKey":"raha","knowledgeType":"occupation","value":"Software engineer","confidence":0.9}
                    ]
                  }
                  """;

        var ingest = new ProjectMemoryIngestResult
        {
            ParseSuccess = true,
            WroteAnyFile = true,
            UpdatedFiles = new[] { "people/raha/profile.md" }
        };

        var text = IngestUserMessageFormatter.Format(ingest, raw, null);

        StringAssert.Contains(text, "Saved for **Raha Mohebbi**:");
        StringAssert.Contains(text, "**Occupation:** Software engineer");
    }

    [TestMethod]
    public void ShouldPreferIngestSummary_false_when_person_query_also_ran()
    {
        var ingest = new ProjectMemoryIngestResult { ParseSuccess = true, WroteAnyFile = true };
        var personas = new[] { "person-extractor", "memory-curator", "person-query" };

        Assert.IsFalse(IngestUserMessageFormatter.ShouldPreferIngestSummary(ingest, personas));
    }

    [TestMethod]
    public void ShouldPreferIngestSummary_true_for_extract_only_turn()
    {
        var ingest = new ProjectMemoryIngestResult { ParseSuccess = true, WroteAnyFile = true };
        var personas = new[] { "person-extractor", "memory-curator" };

        Assert.IsTrue(IngestUserMessageFormatter.ShouldPreferIngestSummary(ingest, personas));
    }

    [TestMethod]
    public void Format_lists_pending_out_of_schema_facts()
    {
        var ingest = new ProjectMemoryIngestResult
        {
            ParseSuccess = true,
            WroteAnyFile = false,
            OutOfSchemaProposals = new[]
            {
                new OutOfSchemaFactProposal
                {
                    UserPromptLine = "raha · pet = golden retriever named Max"
                }
            }
        };

        var text = IngestUserMessageFormatter.Format(ingest, "{\"memoryIntents\":[]}", null);

        StringAssert.Contains(text, "Needs your confirmation");
        StringAssert.Contains(text, "golden retriever");
    }
}
