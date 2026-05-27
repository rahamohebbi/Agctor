using System;
using AgctorSDK.Core.ProjectMemory.Visual.Models;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Human-readable attachment status for playground transcript and API.</summary>
public static class VisualAssetStatusDetail
{
    public static string? ForRecord(VisualAssetRecord? record)
    {
        if (record == null)
            return null;

        var state = record.State?.Trim() ?? "";
        if (string.Equals(state, VisualAssetStates.Failed, StringComparison.OrdinalIgnoreCase))
        {
            var extract = record.Extraction.Status?.Trim();
            if (string.Equals(extract, "failed", StringComparison.OrdinalIgnoreCase))
                return "Vision extract failed. Check Ollama is running and Agctor:LLM:VisionModel is pulled (e.g. ollama pull gemma4:31b).";

            return "Vision analysis failed. Check Ollama is running and the vision model is available.";
        }

        if (string.Equals(state, VisualAssetStates.Inferring, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, VisualAssetStates.Extracting, StringComparison.OrdinalIgnoreCase))
            return "Analyzing photo…";

        if (string.Equals(state, VisualAssetStates.InboxPending, StringComparison.OrdinalIgnoreCase))
            return "Insights ready — review facts in Confirmation inbox.";

        if (string.Equals(state, VisualAssetStates.Extracted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, VisualAssetStates.Ready, StringComparison.OrdinalIgnoreCase))
            return "Photo analyzed.";

        if (string.Equals(state, VisualAssetStates.ReadyForExtract, StringComparison.OrdinalIgnoreCase))
            return "Tagged — waiting for extract.";

        return null;
    }
}
