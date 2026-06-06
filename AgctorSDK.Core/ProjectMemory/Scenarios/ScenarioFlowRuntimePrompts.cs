using System.Text.Json;

namespace AgctorSDK.Core.ProjectMemory.Scenarios;

/// <summary>Prompt text helpers for PRD-024 resume segments.</summary>
public static class ScenarioFlowRuntimePrompts
{
    /// <summary>
    /// Original ChatInput line from the persisted store — used when a resume turn has no new user text
    /// (e.g. domain-event resume after photo extract).
    /// </summary>
    public static string ResolveOriginalUserMessage(ScenarioFlowRuntimeSnapshot snapshot, string? flowJson = null)
    {
        if (snapshot.Store.NodeOutputs.Count == 0)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(flowJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(flowJson);
                if (doc.RootElement.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var node in nodes.EnumerateArray())
                    {
                        if (!node.TryGetProperty("type", out var typeEl)
                            || !string.Equals(typeEl.GetString(), "ChatInput", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!node.TryGetProperty("id", out var idEl))
                            continue;

                        var id = idEl.GetString()?.Trim();
                        if (string.IsNullOrEmpty(id))
                            continue;

                        if (TryGetNodeText(snapshot, id, out var chatText))
                            return chatText;
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to common ids.
            }
        }

        foreach (var id in new[] { "in1", "input", "chatInput" })
        {
            if (TryGetNodeText(snapshot, id, out var text))
                return text;
        }

        return string.Empty;
    }

    private static bool TryGetNodeText(ScenarioFlowRuntimeSnapshot snapshot, string nodeId, out string text)
    {
        text = string.Empty;
        if (!snapshot.Store.NodeOutputs.TryGetValue(nodeId, out var output)
            || string.IsNullOrWhiteSpace(output.Text))
        {
            return false;
        }

        text = output.Text.Trim();
        return true;
    }

    /// <summary>
    /// After photo extract, style coach should use scene summaries — not ask for uploads again.
    /// </summary>
    public static string BuildPostExtractStyleUserMessage(ScenarioFlowRuntimeSnapshot snapshot, string? flowJson)
    {
        var original = ResolveOriginalUserMessage(snapshot, flowJson);
        if (string.IsNullOrWhiteSpace(original))
            original = "Help me with style advice for an upcoming event.";

        var photoCount = ResolvePhotoCount(snapshot);
        var photoLine = photoCount > 0
            ? $"The user uploaded {photoCount} photo(s) in this session. "
            : "The user uploaded multiple photos in this session. ";

        return original
               + "\n\n[System: "
               + photoLine
               + "Vision extract completed. Use the person-visual-context block (all scene summaries and captions) as your primary facts. "
               + "Write ONE unified wedding-guest style response that synthesizes every photo together — outfit, colors, fit, and formality. "
               + "Do not write separate advice per photo or repeat the same bullets for each image. "
               + "Do not ask them to upload photos again.]";
    }

    /// <summary>After all inbox items are reviewed, optional single refinement (not per photo).</summary>
    public static string BuildInboxRefreshStyleUserMessage(ScenarioFlowRuntimeSnapshot? snapshot, string? flowJson)
    {
        var original = snapshot != null
            ? ResolveOriginalUserMessage(snapshot, flowJson)
            : string.Empty;
        if (string.IsNullOrWhiteSpace(original))
            original = "Update my style advice now that we've saved more about me.";

        var photoCount = snapshot != null ? ResolvePhotoCount(snapshot) : 0;
        var photoLine = photoCount > 0
            ? $"Consider all {photoCount} session photos together. "
            : string.Empty;

        return original
               + "\n\n[System: The user finished reviewing the confirmation inbox. "
               + photoLine
               + "Use updated person-memory and person-visual-context. "
               + "Reply with ONE brief refinement (2–4 bullets) — do not repeat prior advice verbatim.]";
    }

    private static int ResolvePhotoCount(ScenarioFlowRuntimeSnapshot snapshot)
    {
        if (snapshot.Store.Facts.TryGetValue("visual.extractedAssetCount", out var count)
            && count is int n
            && n > 0)
        {
            return n;
        }

        if (snapshot.Store.Facts.TryGetValue("visual.extractedAssetCount", out count)
            && count is long ln
            && ln > 0)
        {
            return (int)ln;
        }

        return snapshot.Store.Attachments.AllInRun.Count;
    }
}
