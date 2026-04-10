using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Parsing;
using AgctorSDK.Core.ProjectMemory.Processing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory;

[TestClass]
public sealed class DocumentProjectionServiceTests
{
    [TestMethod]
    public async Task ApplyAsync_ReplaceSection_KeepsMultipleFactsInSameSection()
    {
        var root = Path.Combine(Path.GetTempPath(), "pm-projection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var profilePath = Path.Combine(root, "profile.md");
            await File.WriteAllTextAsync(profilePath,
                "# Raha Profile\n\n## Basic Info\n\n- Occupation: Software Engineer\n");

            var entity = new EntityRecord
            {
                EntityKey = "raha",
                EntityType = "person",
                RootPath = root,
                Metadata = new EntityMetadata { DisplayName = "Raha" },
                DocumentRelativePaths = new List<string> { "profile.md" }
            };

            var svc = new DocumentProjectionService(new DocumentParser());
            var intents = new List<RoutedMemoryIntent>
            {
                new()
                {
                    DocumentTypeId = "profile",
                    SectionTitle = "Basic Info",
                    UpdateMode = "replace_section",
                    FileName = "profile.md",
                    Original = new MemoryIntent
                    {
                        EntityKey = "raha",
                        KnowledgeType = "person",
                        Attribute = "name",
                        Value = "Raha",
                        Confidence = 0.9
                    }
                },
                new()
                {
                    DocumentTypeId = "profile",
                    SectionTitle = "Basic Info",
                    UpdateMode = "replace_section",
                    FileName = "profile.md",
                    Original = new MemoryIntent
                    {
                        EntityKey = "raha",
                        KnowledgeType = "person",
                        Attribute = "age",
                        Value = "45",
                        Confidence = 0.9
                    }
                }
            };

            var res = await svc.ApplyAsync(entity, intents).ConfigureAwait(false);
            Assert.AreEqual(0, res.Issues.Count);
            var updated = await File.ReadAllTextAsync(profilePath).ConfigureAwait(false);

            StringAssert.Contains(updated, "- Name: Raha");
            StringAssert.Contains(updated, "- Age: 45");
            StringAssert.Contains(updated, "- Occupation: Software Engineer");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
