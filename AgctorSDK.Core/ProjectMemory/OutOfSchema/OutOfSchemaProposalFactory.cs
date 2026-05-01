using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>Builds stable proposal ids and user-visible prompts from unrouted intents.</summary>
public static class OutOfSchemaProposalFactory
{
    /// <summary>Deterministic id so the same fact does not duplicate pending/confirmed rows.</summary>
    public static string ComputeProposalId(MemoryIntent intent)
    {
        var attr = intent.Attribute?.Trim() ?? "";
        var canon =
            $"{intent.EntityKey.Trim().ToLowerInvariant()}|{intent.KnowledgeType.Trim().ToLowerInvariant()}|{attr.ToLowerInvariant()}|{intent.Value.Trim()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canon));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string BuildUserPromptLine(MemoryIntent intent)
    {
        var who = string.IsNullOrWhiteSpace(intent.EntityKey) ? "Someone" : intent.EntityKey.Trim();
        var kt = string.IsNullOrWhiteSpace(intent.KnowledgeType) ? "fact" : intent.KnowledgeType.Trim();
        var attr = string.IsNullOrWhiteSpace(intent.Attribute) ? "" : $" ({intent.Attribute.Trim()})";
        var val = intent.Value.Trim();
        if (string.IsNullOrEmpty(val))
            val = "(empty value)";
        return
            $"I found information that is not covered by current memory routing rules: **{who}** — `{kt}{attr}` → \"{val}\". " +
            "Do you want me to store it under **Other information** (generic inbox) for later organization? (yes/no)";
    }

    /// <summary>Turn routing issues into proposals; drops low-confidence noise per runtime options.</summary>
    public static IReadOnlyList<OutOfSchemaFactProposal> FromRouteIssues(
        IReadOnlyList<ValidationIssue> issues,
        OutOfSchemaCaptureOptions? options)
    {
        var opt = options ?? new OutOfSchemaCaptureOptions();
        if (!opt.Enabled)
            return Array.Empty<OutOfSchemaFactProposal>();

        var list = new List<OutOfSchemaFactProposal>();
        foreach (var issue in issues)
        {
            if (!string.Equals(issue.Code, "route_miss", StringComparison.OrdinalIgnoreCase))
                continue;
            if (issue.RelatedIntent == null)
                continue;

            var intent = issue.RelatedIntent;
            if (intent.Confidence < opt.DiscardBelowConfidence)
                continue;

            var disposition = OutOfSchemaConfirmationPolicy.Classify(intent.Confidence, opt);
            if (disposition == null)
                continue;

            list.Add(new OutOfSchemaFactProposal
            {
                ProposalId = ComputeProposalId(intent),
                EntityKey = intent.EntityKey,
                KnowledgeType = intent.KnowledgeType,
                Attribute = intent.Attribute,
                Value = intent.Value,
                Confidence = intent.Confidence,
                Disposition = disposition.Value,
                UserPromptLine = BuildUserPromptLine(intent)
            });
        }

        return list;
    }
}
