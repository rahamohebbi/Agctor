using System.Text;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>Structured routing context appended for the LLM Router (PRD-023 §11.2).</summary>
public sealed class PlaygroundFlowRoutingContext
{
    public bool HasAttachments { get; init; }

    public int AttachmentCount { get; init; }

    public bool AllAnnotated { get; init; }

    public string? AttachmentSummary { get; init; }

    public string? ProjectFocusEntity { get; init; }

    public string? SuggestedIntent { get; init; }

    public string? UserCaption { get; init; }

    public string ToRouterText()
    {
        if (!HasAttachments)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine($"hasAttachments: {HasAttachments.ToString().ToLowerInvariant()}");
        sb.AppendLine($"attachmentCount: {AttachmentCount}");
        sb.AppendLine($"allAnnotated: {AllAnnotated.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(AttachmentSummary))
            sb.AppendLine($"attachmentSummary: {AttachmentSummary.Trim()}");
        if (!string.IsNullOrWhiteSpace(ProjectFocusEntity))
            sb.AppendLine($"projectFocusEntity: {ProjectFocusEntity.Trim()}");
        if (!string.IsNullOrWhiteSpace(SuggestedIntent))
            sb.AppendLine($"suggestedIntent: {SuggestedIntent.Trim()}");
        if (!string.IsNullOrWhiteSpace(UserCaption))
            sb.AppendLine($"userCaption: {UserCaption.Trim()}");
        return sb.ToString().TrimEnd();
    }
}
