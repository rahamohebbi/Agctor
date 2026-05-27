namespace AgctorSDK.Host.Services.Visual;

/// <summary>Browser-safe URLs for visual blobs (file provider cannot use <c>file://</c> in &lt;img&gt;).</summary>
public static class VisualAssetViewUrls
{
    public static string Build(string assetId, string scenarioId, string? projectRoot = null)
    {
        var q = "scenarioId=" + Uri.EscapeDataString(scenarioId.Trim());
        if (!string.IsNullOrWhiteSpace(projectRoot))
            q += "&projectRoot=" + Uri.EscapeDataString(projectRoot.Trim());
        return $"/api/visual/assets/{Uri.EscapeDataString(assetId.Trim())}/view?{q}";
    }
}
