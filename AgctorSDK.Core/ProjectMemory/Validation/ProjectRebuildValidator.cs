using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Validation;

/// <summary>
/// Schema checks: duplicate keys, missing required documents (PRD §13.5).
/// </summary>
public static class ProjectRebuildValidator
{
    public static List<ValidationIssue> Validate(LoadedProjectContext ctx, IReadOnlyList<EntityRecord> entities)
    {
        var issues = new List<ValidationIssue>();
        var dup = entities.GroupBy(e => e.EntityKey, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        foreach (var g in dup)
        {
            issues.Add(new ValidationIssue
            {
                Code = "dup_entity",
                Message = $"Duplicate entity key '{g.Key}'.",
                IsError = true
            });
        }

        var etDefs = ctx.TypeSchema.EntityTypes.EntityTypes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var e in entities)
        {
            if (!etDefs.TryGetValue(e.EntityType, out var def))
                continue;

            foreach (var req in def.RequiredDocuments)
            {
                var p = Path.Combine(e.RootPath, req);
                if (!File.Exists(p))
                {
                    issues.Add(new ValidationIssue
                    {
                        Code = "missing_doc",
                        Message = $"Missing required document '{req}' for entity '{e.EntityKey}'.",
                        Path = p,
                        IsError = true
                    });
                }
            }
        }

        return issues;
    }
}
