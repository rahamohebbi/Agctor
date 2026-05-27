using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Ollama;
using Microsoft.Extensions.Logging;

namespace AgctorSDK.Host.Services;

/// <summary>Warns when the configured Gemma 4 vision model is not present in local Ollama (PRD-023 §8).</summary>
public static class OllamaVisionStartupProbe
{
    public static async Task LogVisionModelAvailabilityAsync(
        IOllamaModelCatalog catalog,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var wanted = OllamaRuntimeConfiguration.GetVisionModel();
        if (string.IsNullOrWhiteSpace(wanted))
            return;

        try
        {
            var models = await catalog.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false);
            var names = models.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.Contains(wanted))
            {
                logger.LogInformation("Vision model '{VisionModel}' is available in Ollama.", wanted);
                return;
            }

            foreach (var fb in OllamaRuntimeConfiguration.GetVisionFallbackModels())
            {
                if (names.Contains(fb))
                {
                    logger.LogWarning(
                        "Vision model '{VisionModel}' not found; fallback '{Fallback}' is available.",
                        wanted,
                        fb);
                    return;
                }
            }

            logger.LogWarning(
                "Vision model '{VisionModel}' not found in Ollama. Run: ollama pull {VisionModel}",
                wanted,
                wanted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not verify Ollama vision model '{VisionModel}'.", wanted);
        }
    }
}
