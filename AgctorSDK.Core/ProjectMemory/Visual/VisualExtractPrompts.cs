using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgctorSDK.Core.ProjectMemory.Visual.Models;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Versioned prompts for Gemma 4 vision (PRD-023 §8).</summary>
public static class VisualExtractPrompts
{
    public const string ExtractVersion = "visual-extract-v1";
    public const string InferVersion = "visual-infer-v1";
    public const string QuerySceneVersion = "visual-query-scene-v1";

    public static string QuerySceneSystemPrompt =>
        """
        You describe personal photos for a memory assistant.
        Reply with plain text only (1-3 sentences). Mention visible activity, setting, clothing, companions, and notable objects.
        Do not wrap in JSON or markdown fences.
        """;

    public static string ExtractSystemPrompt =>
        """
        You are a visual memory extractor for a personal knowledge base.
        Respond with ONLY valid JSON. No markdown fences, no thinking tags, no commentary.

        Output schema:
        {"sceneSummary":"one concise sentence: visible activity, setting, clothing, companions","memoryIntents":[{"entityKey":"slug","knowledgeType":"physical_attribute|preference|observation|profile_fact|family_role","attribute":"optional","value":"text","confidence":0.0}]}

        Rules:
        - sceneSummary must describe what is visibly happening (not just who the person is).
        - Infer only what is clearly visible in the image or stated in the user caption.
        - Use lowercase entityKey slugs for people.
        - Lower confidence when uncertain.
        """;

    public static string InferSystemPrompt =>
        """
        You identify people and intent in a personal photo for a knowledge base.
        Respond with ONLY valid JSON:
        {"entityKeys":["slug"],"confidence":0.0,"rationale":"brief","suggestedIntent":"style|fitness|general"}
        No markdown, no thinking tags.
        """;

    public static string BuildExtractUserText(
        VisualAssetRecord record,
        string? userMessage,
        string? focusEntityKey)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Image attached above this text]");
        AppendContext(sb, record, userMessage, focusEntityKey);
        sb.AppendLine();
        sb.AppendLine("Extract memoryIntents JSON for this photo.");
        return sb.ToString().Trim();
    }

    public static string BuildInferUserText(
        VisualAssetRecord record,
        string? userMessage,
        string? focusEntityKey)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Image attached above this text]");
        AppendContext(sb, record, userMessage, focusEntityKey);
        sb.AppendLine();
        sb.AppendLine("Who is in this photo and what is the suggested intent?");
        return sb.ToString().Trim();
    }

    public static string BuildQuerySceneUserText(
        VisualAssetRecord record,
        string? userMessage,
        string? focusEntityKey)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Image attached above this text]");
        AppendContext(sb, record, userMessage, focusEntityKey);
        sb.AppendLine();
        sb.AppendLine("Describe what is happening in this photo.");
        if (!string.IsNullOrWhiteSpace(userMessage))
            sb.AppendLine("User question: " + userMessage.Trim());
        return sb.ToString().Trim();
    }

    private static void AppendContext(
        StringBuilder sb,
        VisualAssetRecord record,
        string? userMessage,
        string? focusEntityKey)
    {
        if (!string.IsNullOrWhiteSpace(record.Context.UserCaption))
            sb.AppendLine("User caption: " + record.Context.UserCaption.Trim());
        if (!string.IsNullOrWhiteSpace(record.Context.Occasion))
            sb.AppendLine("Occasion: " + record.Context.Occasion.Trim());
        if (!string.IsNullOrWhiteSpace(userMessage))
            sb.AppendLine("User message: " + userMessage.Trim());
        if (!string.IsNullOrWhiteSpace(focusEntityKey))
            sb.AppendLine("Project focus entity: " + focusEntityKey.Trim());

        if (record.Subjects.Count > 0)
        {
            var subjects = string.Join(", ",
                record.Subjects.Select(s =>
                    string.IsNullOrWhiteSpace(s.DisplayName)
                        ? $"{s.EntityKey}({s.Role})"
                        : $"{s.DisplayName}/{s.EntityKey}({s.Role})"));
            sb.AppendLine("Tagged subjects: " + subjects);
        }
    }
}
