using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>Builds <see cref="PlaygroundFlowRoutingContext"/> from a playground turn.</summary>
public static class PlaygroundFlowRoutingContextBuilder
{
    public static PlaygroundFlowRoutingContext Build(
        int attachmentCount,
        string? userMessage,
        string? focusEntityKey,
        IReadOnlyList<PlaygroundStreamAttachmentDto>? attachments = null)
    {
        if (attachmentCount <= 0)
        {
            return new PlaygroundFlowRoutingContext
            {
                HasAttachments = false,
                AttachmentCount = 0
            };
        }

        var tagged = attachments?.Count(a => !string.IsNullOrWhiteSpace(a.EntityKey)) ?? 0;
        var intent = PlaygroundFlowPreRouter.InferSuggestedIntent(userMessage);
        var summary = BuildAttachmentSummary(attachmentCount, attachments, userMessage);

        return new PlaygroundFlowRoutingContext
        {
            HasAttachments = true,
            AttachmentCount = attachmentCount,
            AllAnnotated = tagged >= attachmentCount,
            AttachmentSummary = summary,
            ProjectFocusEntity = focusEntityKey?.Trim(),
            SuggestedIntent = intent,
            UserCaption = string.IsNullOrWhiteSpace(userMessage) ? null : userMessage.Trim()
        };
    }

    private static string BuildAttachmentSummary(
        int count,
        IReadOnlyList<PlaygroundStreamAttachmentDto>? attachments,
        string? userMessage)
    {
        var parts = new List<string> { $"{count} image(s)" };
        if (attachments != null)
        {
            var keys = attachments
                .Select(a => a.EntityKey?.Trim())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keys.Count > 0)
                parts.Add("subjects: " + string.Join(", ", keys));
        }

        if (!string.IsNullOrWhiteSpace(userMessage))
            parts.Add($"caption: \"{Truncate(userMessage.Trim(), 120)}\"");
        return string.Join("; ", parts);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
