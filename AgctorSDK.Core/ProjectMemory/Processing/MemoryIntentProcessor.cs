using System;
using System.Collections.Generic;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Processing;

public sealed class MemoryIntentProcessor : IMemoryIntentProcessor
{
    public IReadOnlyList<RoutedMemoryIntent> Route(LoadedProjectContext ctx, IReadOnlyList<MemoryIntent> intents, out IReadOnlyList<ValidationIssue> issues)
    {
        var list = new List<RoutedMemoryIntent>();
        var issueList = new List<ValidationIssue>();
        var routing = ctx.TypeSchema.Routing.RoutingRules;
        var docTypes = ctx.TypeSchema.DocumentTypes.DocumentTypes.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var intent in intents)
        {
            var rule = routing.FirstOrDefault(r =>
                string.Equals(r.When.KnowledgeType, intent.KnowledgeType, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(r.When.Attribute)
                    ? true
                    : string.Equals(r.When.Attribute, intent.Attribute, StringComparison.OrdinalIgnoreCase)));

            if (rule == null)
            {
                issueList.Add(new ValidationIssue
                {
                    Code = "route_miss",
                    Message = $"No routing rule for knowledgeType '{intent.KnowledgeType}' (attribute '{intent.Attribute ?? ""}').",
                    IsError = true,
                    RelatedIntent = intent
                });
                continue;
            }

            if (!docTypes.TryGetValue(rule.Target.Document, out var dt))
            {
                issueList.Add(new ValidationIssue
                {
                    Code = "doc_type",
                    Message = $"Document type '{rule.Target.Document}' not defined.",
                    IsError = true
                });
                continue;
            }

            list.Add(new RoutedMemoryIntent
            {
                Original = intent,
                DocumentTypeId = dt.Id,
                SectionTitle = rule.Target.Section,
                UpdateMode = dt.UpdateMode,
                FileName = dt.FileName
            });
        }

        issues = issueList;
        return list;
    }
}
